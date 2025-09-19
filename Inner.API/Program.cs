using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Inner API", Version = "v1" });
});

builder.Services.AddDbContext<PokemonDb>(opt =>
   opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(
   ConnectionMultiplexer.Connect("localhost:6379"));

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

// Ensure database is created and up to date
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PokemonDb>();
    try
    {
        context.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database creation error: {ex.Message}");
        // Log the error but continue - the app can still run for other endpoints
    }
}

// GET /pokemon/{name}  
app.MapGet("/pokemon/{name}", async (string name, PokemonDb db, IHttpClientFactory factory, IConnectionMultiplexer redis) =>
{
    var cache = redis.GetDatabase();
    string cacheKey = $"pokemon:{name.ToLower()}";

    var cached = await cache.StringGetAsync(cacheKey);
    if (cached.HasValue)
        return Results.Ok(System.Text.Json.JsonSerializer.Deserialize<Pokemon>(cached!));

    var dbPokemon = await db.Pokemons.FirstOrDefaultAsync(p => p.Name == name.ToLower());
    if (dbPokemon != null)
    {
        await cache.StringSetAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(dbPokemon));
        return Results.Ok(dbPokemon);
    }

    var client = factory.CreateClient();
    var pokeApiResp = await client.GetFromJsonAsync<PokeApiResponse>($"https://pokeapi.co/api/v2/pokemon/{name.ToLower()}");
    if (pokeApiResp is null) return Results.NotFound();

    var pokemon = new Pokemon { Name = pokeApiResp.name, Height = pokeApiResp.height, Weight = pokeApiResp.weight };

    db.Pokemons.Add(pokemon);
    await db.SaveChangesAsync();

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

class PokemonDb : DbContext
{
    public PokemonDb(DbContextOptions<PokemonDb> opts) : base(opts) { }
    public DbSet<Pokemon> Pokemons => Set<Pokemon>();
}
