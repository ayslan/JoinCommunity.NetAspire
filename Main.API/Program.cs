var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/", () => "Main API is running!")
    .WithName("GetRoot")
    .WithSummary("Check if API is running")
    .WithDescription("Returns a simple message indicating the API is running");

app.MapGet("/summary/{name}", async (string name, InnerApiClient client) =>
{
    if (string.IsNullOrEmpty(name))
        return Results.BadRequest("Name is required.");

    var pokemon = await client.GetPokemonAsync(name);
    if (pokemon is null)
        return Results.NotFound();

    return Results.Json(new
    {
        Info = $"{pokemon.Name} - Height: {pokemon.Height}, Weight: {pokemon.Weight}"
    });
})
.WithName("GetPokemonSummary")
.WithSummary("Get Pokemon summary")
.WithDescription("Retrieves a formatted summary of Pokemon data from the Inner API");

app.Run();

public record PokemonDto(string Name, int Height, int Weight);

public class InnerApiClient
{
    private readonly HttpClient _http;

    public InnerApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PokemonDto?> GetPokemonAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/pokemon/{name}", cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<PokemonDto>(cancellationToken: cancellationToken);
    }
}

