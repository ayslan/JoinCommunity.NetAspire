using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Data;
using System.Diagnostics;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

// Configure logging with different levels
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure OpenTelemetry for Aspire
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Inner.API"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Inner.API") // Add our custom ActivitySource
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

// Add service discovery for Aspire
builder.Services.AddServiceDiscovery();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Inner API", Version = "v1" });
});

// Register SQL connection string
//builder.Services.AddScoped<SqlConnection>(serviceProvider =>
//{
//    var config = serviceProvider.GetRequiredService<IConfiguration>();
//    var connectionString = config.GetConnectionString("SqlServer")
//        ?? throw new InvalidOperationException("SqlServer connection string is not configured.");

//    return new SqlConnection(connectionString);
//});

builder.AddSqlServerClient("sqlserver");

// Register database service
builder.Services.AddScoped<IPokemonRepository, PokemonRepository>();

//builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
//{
//    try
//    {
//        return ConnectionMultiplexer.Connect("localhost:6379");
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"Redis connection failed: {ex.Message}");
//        // Return a dummy connection multiplexer for development when Redis is not available
//        throw new InvalidOperationException("Redis is not available. Please ensure Redis is running or configure a fallback.");
//    }
//});

builder.AddRedisClient("redis");

builder.Services.AddHttpClient();

var app = builder.Build();

// Create ActivitySource for custom tracing
var activitySource = new ActivitySource("Inner.API");

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🚀 Inner API is starting up at {Timestamp}", DateTime.UtcNow);

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Inner API v1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<IPokemonRepository>();
    try
    {
        logger.LogInformation("🔧 Starting database initialization...");
        await repository.InitializeDatabaseAsync();
        logger.LogInformation("✅ Database initialized successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Database initialization failed: {ErrorMessage}", ex.Message);
        // Log the error but continue - the app can still run for other endpoints
    }
} 

// GET /pokemon/{name}  
app.MapGet("/pokemon/{name}", async ([FromRoute] string name, IPokemonRepository repository, IHttpClientFactory factory, IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("Pokemon.Search");
    activity?.SetTag("pokemon.name", name);
    
    logger.LogInformation("🔍 Searching for Pokemon: {PokemonName} at {RequestTime}", name, DateTime.UtcNow);
    
    var cache = redis.GetDatabase();
    string cacheKey = $"pokemon:{name.ToLower()}";

    // Check cache first
    using var cacheActivity = activitySource.StartActivity("Pokemon.Cache.Check");
    cacheActivity?.SetTag("cache.key", cacheKey);
    
    logger.LogDebug("📦 Checking cache for key: {CacheKey}", cacheKey);
    var cached = await cache.StringGetAsync(cacheKey);
    if (cached.HasValue)
    {
        cacheActivity?.SetTag("cache.hit", true);
        logger.LogInformation("⚡ Cache hit for Pokemon: {PokemonName}", name);
        activity?.SetTag("data.source", "cache");
        return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<Pokemon>(cached!));
    }
    
    cacheActivity?.SetTag("cache.hit", false);
    logger.LogInformation("❌ Cache miss for Pokemon: {PokemonName}, checking database...", name);

    // Check database
    using var dbActivity = activitySource.StartActivity("Pokemon.Database.Query");
    dbActivity?.SetTag("pokemon.name", name);
    
    var dbPokemon = await repository.GetPokemonByNameAsync(name.ToLower());
    if (dbPokemon != null)
    {
        dbActivity?.SetTag("database.hit", true);
        logger.LogInformation("🗄️ Found Pokemon in database: {PokemonName} (ID: {PokemonId})", dbPokemon.Name, dbPokemon.Id);
        
        using var cacheStoreActivity = activitySource.StartActivity("Pokemon.Cache.Store");
        await cache.StringSetAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(dbPokemon));
        logger.LogDebug("💾 Cached Pokemon data for future requests");
        
        activity?.SetTag("data.source", "database");
        return Results.Ok(dbPokemon);
    }

    dbActivity?.SetTag("database.hit", false);
    logger.LogInformation("🌐 Pokemon not found in database, fetching from external API: {PokemonName}", name);
    
    // Check external API
    using var externalApiActivity = activitySource.StartActivity("Pokemon.ExternalAPI.Fetch");
    externalApiActivity?.SetTag("external.api", "pokeapi.co");
    externalApiActivity?.SetTag("pokemon.name", name);
    
    var client = factory.CreateClient();

    try
    {
        var pokeApiResp = await client.GetFromJsonAsync<PokeApiResponse>($"https://pokeapi.co/api/v2/pokemon/{name.ToLower()}");
        if (pokeApiResp is null) 
        {
            externalApiActivity?.SetTag("external.api.success", false);
            logger.LogWarning("⚠️ Pokemon not found in external API: {PokemonName}", name);
            return Results.NotFound();
        }

        externalApiActivity?.SetTag("external.api.success", true);
        var pokemon = new Pokemon { Name = pokeApiResp.name, Height = pokeApiResp.height, Weight = pokeApiResp.weight };

        logger.LogInformation("✅ Successfully fetched Pokemon from external API: {PokemonName} (Height: {Height}, Weight: {Weight})", 
            pokemon.Name, pokemon.Height, pokemon.Weight);

        // Save to database
        using var dbSaveActivity = activitySource.StartActivity("Pokemon.Database.Insert");
        await repository.AddPokemonAsync(pokemon);
        logger.LogInformation("💾 Saved Pokemon to database: {PokemonName} (ID: {PokemonId})", pokemon.Name, pokemon.Id);

        // Cache the result
        using var cacheSaveActivity = activitySource.StartActivity("Pokemon.Cache.Store");
        await cache.StringSetAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(pokemon));
        logger.LogDebug("📦 Cached Pokemon data for future requests");

        activity?.SetTag("data.source", "external-api");
        return Results.Ok(pokemon);
    }
    catch (HttpRequestException ex)
    {
        externalApiActivity?.SetTag("external.api.success", false);
        externalApiActivity?.SetTag("error.message", ex.Message);
        logger.LogError(ex, "🚨 HTTP error while fetching Pokemon from external API: {PokemonName} - {ErrorMessage}", name, ex.Message);
        return Results.Problem("Failed to fetch Pokemon from external service");
    }
    catch (Exception ex)
    {
        activity?.SetTag("error", true);
        activity?.SetTag("error.message", ex.Message);
        logger.LogError(ex, "💥 Unexpected error while processing Pokemon request: {PokemonName} - {ErrorMessage}", name, ex.Message);
        return Results.Problem("An unexpected error occurred");
    }
})
.WithName("GetPokemon")
.WithSummary("Get Pokemon by name")
.WithDescription("Retrieves Pokemon data from cache, database, or external API");

// Add a health check endpoint
app.MapGet("/health", (ILogger<Program> logger) =>
{
    logger.LogInformation("🩺 Inner API health check at {RequestTime}", DateTime.UtcNow);
    
    var healthStatus = new
    {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        DatabaseConnected = true, // In real scenario, you'd check actual DB connection
        RedisConnected = true,    // In real scenario, you'd check actual Redis connection
        Version = "1.0.0"
    };
    
    logger.LogInformation("✅ Inner API health check completed - All systems operational");
    return Results.Ok(healthStatus);
})
.WithName("HealthCheck")
.WithSummary("Check Inner API health")
.WithDescription("Returns health status of Inner API and its dependencies");

// Background logging for demonstration
_ = Task.Run(async () =>
{
    var backgroundLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var random = new Random();
    
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(1.5)); // Log every 1.5 minutes
        
        var logType = random.Next(1, 6);
        switch (logType)
        {
            case 1:
                backgroundLogger.LogDebug("🔄 Background: Cache cleanup routine executed");
                break;
            case 2:
                backgroundLogger.LogInformation("📊 Background: Database connections - Active: {ActiveConnections}, Pool: {PoolSize}", 
                    random.Next(1, 10), random.Next(10, 50));
                break;
            case 3:
                backgroundLogger.LogInformation("⚡ Background: Redis operations - Commands/sec: {RedisOps}", 
                    random.Next(50, 500));
                break;
            case 4:
                backgroundLogger.LogWarning("⚠️ Background: Slow query detected - Duration: {QueryDuration}ms", 
                    random.Next(1000, 5000));
                break;
            case 5:
                backgroundLogger.LogError("🚨 Background: Simulated error for demonstration - Connection timeout to external service");
                break;
        }
    }
});

logger.LogInformation("🔄 Inner API background monitoring started");

app.Run();

record Pokemon
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Height { get; set; }
    public int Weight { get; set; }
}

record PokeApiResponse(string name, int height, int weight);

interface IPokemonRepository
{
    Task InitializeDatabaseAsync();
    Task<Pokemon?> GetPokemonByNameAsync(string name);
    Task<Pokemon> AddPokemonAsync(Pokemon pokemon);
}

class PokemonRepository : IPokemonRepository
{
    private readonly SqlConnection _connection;
    private readonly ILogger<PokemonRepository> _logger;

    public PokemonRepository(SqlConnection connection, ILogger<PokemonRepository> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task InitializeDatabaseAsync()
    {
        _logger.LogInformation("🔧 Starting database initialization process...");
        
        // Read and execute database creation scripts
        var scriptsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SqlScripts");
        
        if (!Directory.Exists(scriptsPath))
        {
            _logger.LogWarning("⚠️ SqlScripts directory not found at: {ScriptsPath}", scriptsPath);
            return;
        }

        var scriptFiles = Directory.GetFiles(scriptsPath, "*.sql").OrderBy(f => f).ToArray();
        _logger.LogInformation("📄 Found {ScriptCount} SQL script files to execute", scriptFiles.Length);
        
        foreach (var scriptFile in scriptFiles)
        {
            _logger.LogDebug("📜 Executing SQL script: {ScriptFile}", Path.GetFileName(scriptFile));
            var script = await File.ReadAllTextAsync(scriptFile);
            await ExecuteScriptAsync(script);
            _logger.LogInformation("✅ Successfully executed script: {ScriptFile}", Path.GetFileName(scriptFile));
        }
        
        _logger.LogInformation("🎉 Database initialization completed successfully");
    }

    public async Task<Pokemon?> GetPokemonByNameAsync(string name)
    {
        _logger.LogDebug("🔍 Searching database for Pokemon: {PokemonName}", name);
        
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT Id, Name, Height, Weight FROM Pokemons WHERE Name = @Name", 
            _connection);
        command.Parameters.AddWithValue("@Name", name);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var pokemon = new Pokemon
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Height = reader.GetInt32(reader.GetOrdinal("Height")),
                Weight = reader.GetInt32(reader.GetOrdinal("Weight"))
            };
            
            _logger.LogInformation("✅ Found Pokemon in database: {PokemonName} (ID: {PokemonId})", pokemon.Name, pokemon.Id);
            return pokemon;
        }

        _logger.LogDebug("❌ Pokemon not found in database: {PokemonName}", name);
        return null;
    }

    public async Task<Pokemon> AddPokemonAsync(Pokemon pokemon)
    {
        _logger.LogInformation("💾 Adding new Pokemon to database: {PokemonName} (Height: {Height}, Weight: {Weight})", 
            pokemon.Name, pokemon.Height, pokemon.Weight);
            
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        using var command = new SqlCommand(
            "INSERT INTO Pokemons (Name, Height, Weight) OUTPUT INSERTED.Id VALUES (@Name, @Height, @Weight)", 
            _connection);
        command.Parameters.AddWithValue("@Name", pokemon.Name);
        command.Parameters.AddWithValue("@Height", pokemon.Height);
        command.Parameters.AddWithValue("@Weight", pokemon.Weight);

        var id = (int)(await command.ExecuteScalarAsync() ?? 0);
        pokemon.Id = id;

        _logger.LogInformation("✅ Successfully added Pokemon to database: {PokemonName} with ID: {PokemonId}", pokemon.Name, pokemon.Id);
        return pokemon;
    }

    private async Task ExecuteScriptAsync(string script)
    {
        _logger.LogDebug("⚙️ Executing SQL script batch...");
        
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        // Split script by GO statements and execute each batch
        var batches = script.Split(new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "\nGO", "GO\n" }, 
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var batch in batches)
        {
            var trimmedBatch = batch.Trim();
            if (string.IsNullOrEmpty(trimmedBatch)) continue;

            _logger.LogTrace("📝 Executing SQL batch: {BatchPreview}...", 
                trimmedBatch.Length > 50 ? trimmedBatch.Substring(0, 50) + "..." : trimmedBatch);
                
            using var command = new SqlCommand(trimmedBatch, _connection);
            await command.ExecuteNonQueryAsync();
        }
        
        _logger.LogDebug("✅ SQL script batch execution completed");
    }
}
