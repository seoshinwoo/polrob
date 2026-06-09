using polrob.Server.Hubs;
using polrob.Server.Network;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();

if (builder.Configuration.GetValue<bool>("EnableGameServer", true))
{
    builder.Services.AddHostedService<GameNetworkServer>(); // Add Custom raw TCP/UDP server
}

var cosmosDbConnString = new[]
    {
        builder.Configuration.GetConnectionString("CosmosDb"),
        builder.Configuration["CosmosDb:ConnectionString"],
        builder.Configuration["COSMOSDB_CONNECTIONSTRING"]
    }
    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
    ?? throw new InvalidOperationException(
        "Cosmos DB connection string is missing. Set ConnectionStrings:CosmosDb with a full value like 'AccountEndpoint=...;AccountKey=...;' or set COSMOSDB_CONNECTIONSTRING.");

if (!cosmosDbConnString.Contains("AccountEndpoint=", StringComparison.OrdinalIgnoreCase)
    || !cosmosDbConnString.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Cosmos DB connection string is invalid. Use the full connection string from Azure Portal > Cosmos DB account > Keys > Primary Connection String. An endpoint URL alone is not enough.");
}

builder.Services.AddSingleton<LoginDbService>(sp => new LoginDbService(cosmosDbConnString));
builder.Services.AddSingleton<BotIdentityService>();
builder.Services.AddSingleton<GameRoomService>();

var app = builder.Build();
using (var scope = app.Services.CreateAsyncScope())
{
    var cosmosService = scope.ServiceProvider.GetRequiredService<LoginDbService>();
    await cosmosService.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();
app.MapHub<GameRoomHub>("/hubs/game-room");

app.Run();
