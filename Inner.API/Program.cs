using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Inner API", Version = "v1" });
});

// Register SQL connection string
builder.Services.AddScoped<SqlConnection>(serviceProvider =>
{
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("SqlServer connection string is not configured.");

    return new SqlConnection(connectionString);
});

// Register database service
builder.Services.AddScoped<IPokemonRepository, PokemonRepository>();

builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
    try
    {
        return ConnectionMultiplexer.Connect("localhost:6379");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Redis connection failed: {ex.Message}");
        // Return a dummy connection multiplexer for development when Redis is not available
        throw new InvalidOperationException("Redis is not available. Please ensure Redis is running or configure a fallback.");
    }
});

builder.Services.AddHttpClient();

var app = builder.Build();

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
        await repository.InitializeDatabaseAsync();
        Console.WriteLine("Database initialized successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization error: {ex.Message}");
        // Log the error but continue - the app can still run for other endpoints
    }
}

// GET /pokemon/{name}  
app.MapGet("/pokemon/{name}", async ([FromRoute] string name, IPokemonRepository repository, IHttpClientFactory factory, IConnectionMultiplexer redis) =>
{
    var cache = redis.GetDatabase();
    string cacheKey = $"pokemon:{name.ToLower()}";

    var cached = await cache.StringGetAsync(cacheKey);
    if (cached.HasValue)
        return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<Pokemon>(cached!));

    var dbPokemon = await repository.GetPokemonByNameAsync(name.ToLower());
    if (dbPokemon != null)
    {
        await cache.StringSetAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(dbPokemon));
        return Results.Ok(dbPokemon);
    }

    var client = factory.CreateClient();

    var pokeApiResp = await client.GetFromJsonAsync<PokeApiResponse>($"https://pokeapi.co/api/v2/pokemon/{name.ToLower()}");
    if (pokeApiResp is null) return Results.NotFound();

    var pokemon = new Pokemon { Name = pokeApiResp.name, Height = pokeApiResp.height, Weight = pokeApiResp.weight };

    await repository.AddPokemonAsync(pokemon);

    await cache.StringSetAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(pokemon));

    return Results.Ok(pokemon);
})
.WithName("GetPokemon")
.WithSummary("Get Pokemon by name")
.WithDescription("Retrieves Pokemon data from cache, database, or external API");

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

    public PokemonRepository(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task InitializeDatabaseAsync()
    {
        // Read and execute database creation scripts
        var scriptsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SqlScripts");

        if (!Directory.Exists(scriptsPath))
        {
            Console.WriteLine($"SqlScripts directory not found at: {scriptsPath}");
            return;
        }

        var scriptFiles = Directory.GetFiles(scriptsPath, "*.sql").OrderBy(f => f).ToArray();

        foreach (var scriptFile in scriptFiles)
        {
            var script = await File.ReadAllTextAsync(scriptFile);
            await ExecuteScriptAsync(script);
            Console.WriteLine($"Executed script: {Path.GetFileName(scriptFile)}");
        }
    }

    public async Task<Pokemon?> GetPokemonByNameAsync(string name)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT Id, Name, Height, Weight FROM Pokemons WHERE Name = @Name",
            _connection);
        command.Parameters.AddWithValue("@Name", name);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Pokemon
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Height = reader.GetInt32(reader.GetOrdinal("Height")),
                Weight = reader.GetInt32(reader.GetOrdinal("Weight"))
            };
        }

        return null;
    }

    public async Task<Pokemon> AddPokemonAsync(Pokemon pokemon)
    {
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

        return pokemon;
    }

    private async Task ExecuteScriptAsync(string script)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        // Split script by GO statements and execute each batch
        var batches = script.Split(new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "\nGO", "GO\n" },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var batch in batches)
        {
            var trimmedBatch = batch.Trim();
            if (string.IsNullOrEmpty(trimmedBatch)) continue;

            using var command = new SqlCommand(trimmedBatch, _connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}