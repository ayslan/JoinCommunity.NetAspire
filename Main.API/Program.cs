using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Configure logging with different levels
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure OpenTelemetry for Aspire
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Main.API"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Main.API") // Add our custom ActivitySource
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

// Add service discovery
builder.Services.AddServiceDiscovery();

// Configure HttpClient to use service discovery
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddServiceDiscovery();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Main API", Version = "v1" });
});

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

//builder.Services.AddHttpClient<InnerApiClient>(client =>
//{
//    client.BaseAddress = new Uri("https://localhost:7020");  
//});

builder.Services.AddHttpClient<InnerApiClient>(client =>
{
    client.BaseAddress = new Uri("http://inner-api");
});

var app = builder.Build();

// Create ActivitySource for custom tracing
var activitySource = new ActivitySource("Main.API");

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🚀 Main API is starting up at {Timestamp}", DateTime.UtcNow);

// Use CORS middleware
app.UseCors("AllowReactApp");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Main API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.MapGet("/", (ILogger<Program> logger) => 
{
    logger.LogInformation("🏠 Root endpoint accessed at {RequestTime}", DateTime.UtcNow);
    return "Main API is running!";
})
    .WithName("GetRoot")
    .WithSummary("Check if API is running")
    .WithDescription("Returns a simple message indicating the API is running");

// Health check endpoint with detailed logging
app.MapGet("/health", (ILogger<Program> logger) =>
{
    logger.LogInformation("🩺 Health check endpoint accessed at {RequestTime}", DateTime.UtcNow);
    
    var healthStatus = new
    {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        Version = "1.0.0",
        Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
    };
    
    logger.LogInformation("✅ Health check completed successfully - Status: {Status}, Environment: {Environment}", 
        healthStatus.Status, healthStatus.Environment);
        
    return Results.Ok(healthStatus);
})
.WithName("HealthCheck")
.WithSummary("Check API health status")
.WithDescription("Returns health status information for monitoring");

app.MapGet("/summary/{name}", async (string name, InnerApiClient client, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("Pokemon.Summary");
    activity?.SetTag("pokemon.name", name);
    
    logger.LogInformation("📊 Summary request received for Pokemon: {PokemonName} at {RequestTime}", name, DateTime.UtcNow);
    
    if (string.IsNullOrEmpty(name))
    {
        activity?.SetTag("validation.failed", true);
        logger.LogWarning("⚠️ Invalid request: Pokemon name is empty or null");
        return Results.BadRequest("Name is required.");
    }

    activity?.SetTag("validation.passed", true);

    try
    {
        using var innerApiActivity = activitySource.StartActivity("InnerAPI.Call");
        innerApiActivity?.SetTag("service.name", "Inner.API");
        innerApiActivity?.SetTag("pokemon.name", name);
        
        logger.LogInformation("🔗 Calling Inner API to fetch Pokemon: {PokemonName}", name);
        var pokemon = await client.GetPokemonAsync(name);
        
        if (pokemon is null)
        {
            innerApiActivity?.SetTag("inner.api.success", false);
            activity?.SetTag("result", "not_found");
            logger.LogWarning("❌ Pokemon not found: {PokemonName}", name);
            return Results.NotFound();
        }

        innerApiActivity?.SetTag("inner.api.success", true);
        activity?.SetTag("result", "success");

        var summary = new
        {
            Info = $"{pokemon.Name} - Height: {pokemon.Height}, Weight: {pokemon.Weight}"
        };

        activity?.SetTag("pokemon.height", pokemon.Height);
        activity?.SetTag("pokemon.weight", pokemon.Weight);

        logger.LogInformation("✅ Successfully generated summary for Pokemon: {PokemonName} - {Summary}", 
            pokemon.Name, summary.Info);

        return Results.Json(summary);
    }
    catch (HttpRequestException ex)
    {
        activity?.SetTag("error", true);
        activity?.SetTag("error.type", "http_request");
        activity?.SetTag("error.message", ex.Message);
        logger.LogError(ex, "🚨 HTTP error while calling Inner API for Pokemon: {PokemonName} - {ErrorMessage}", name, ex.Message);
        return Results.Problem("Failed to retrieve Pokemon data from inner service");
    }
    catch (Exception ex)
    {
        activity?.SetTag("error", true);
        activity?.SetTag("error.type", "unexpected");
        activity?.SetTag("error.message", ex.Message);
        logger.LogError(ex, "💥 Unexpected error while processing summary request for Pokemon: {PokemonName} - {ErrorMessage}", name, ex.Message);
        return Results.Problem("An unexpected error occurred while processing your request");
    }
})
.WithName("GetPokemonSummary")
.WithSummary("Get Pokemon summary")
.WithDescription("Retrieves a formatted summary of Pokemon data from the Inner API");

// Start a background service to generate periodic logs for demonstration
_ = Task.Run(async () =>
{
    var backgroundLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var random = new Random();
    
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(2)); // Log every 2 minutes
        
        var logLevel = random.Next(1, 5);
        switch (logLevel)
        {
            case 1:
                backgroundLogger.LogTrace("🔍 Background trace: System performing routine checks...");
                break;
            case 2:
                backgroundLogger.LogDebug("🐛 Background debug: Cache statistics - Hits: {CacheHits}, Misses: {CacheMisses}", 
                    random.Next(100, 1000), random.Next(10, 100));
                break;
            case 3:
                backgroundLogger.LogInformation("📈 Background info: System metrics - Memory: {MemoryUsage}MB, CPU: {CpuUsage}%", 
                    random.Next(50, 200), random.Next(10, 80));
                break;
            case 4:
                backgroundLogger.LogWarning("⚠️ Background warning: High memory usage detected - {MemoryUsage}MB", 
                    random.Next(200, 500));
                break;
        }
    }
});

logger.LogInformation("🎯 Background logging service started for demonstration purposes");

app.Run();

public record PokemonDto(string Name, int Height, int Weight);

public class InnerApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InnerApiClient> _logger;

    public InnerApiClient(HttpClient http, ILogger<InnerApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PokemonDto?> GetPokemonAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🌐 Making HTTP request to Inner API for Pokemon: {PokemonName}", name);
        
        try
        {
            var response = await _http.GetAsync($"/pokemon/{name}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("⚠️ Inner API returned non-success status: {StatusCode} for Pokemon: {PokemonName}", 
                    response.StatusCode, name);
                return null;
            }

            var pokemon = await response.Content.ReadFromJsonAsync<PokemonDto>(cancellationToken: cancellationToken);
            
            if (pokemon != null)
            {
                _logger.LogInformation("✅ Successfully retrieved Pokemon from Inner API: {PokemonName} (Height: {Height}, Weight: {Weight})", 
                    pokemon.Name, pokemon.Height, pokemon.Weight);
            }
            else
            {
                _logger.LogWarning("❌ Failed to deserialize Pokemon response from Inner API for: {PokemonName}", name);
            }
            
            return pokemon;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "⏱️ Request to Inner API timed out for Pokemon: {PokemonName}", name);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "🚨 HTTP error calling Inner API for Pokemon: {PokemonName} - {ErrorMessage}", name, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Unexpected error calling Inner API for Pokemon: {PokemonName} - {ErrorMessage}", name, ex.Message);
            throw;
        }
    }
}

