using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using polrob.Server.Hubs;
using polrob.Server.Network;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();

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

builder.Services.Configure<CosmosDbOptions>(
    builder.Configuration.GetSection(CosmosDbOptions.SectionName));
builder.Services.AddSingleton(_ => new CosmosClient(
    cosmosDbConnString,
    new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        },
        // Gateway mode uses HTTPS (port 443), which works on networks where the
        // dedicated TCP ports required by Direct mode are blocked.
        ConnectionMode = ConnectionMode.Gateway
    }));
builder.Services.AddSingleton<UserDbService>();
builder.Services.AddSingleton<GameRecordDbService>();
builder.Services.AddSingleton<IGameRecordStatsReader>(
    sp => sp.GetRequiredService<GameRecordDbService>());
builder.Services.AddSingleton<GameRecordWriter>();
builder.Services.AddSingleton<IGameRecordQueue>(sp => sp.GetRequiredService<GameRecordWriter>());
builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<GameRecordWriter>());
builder.Services.AddHostedService<GameRecordReconciler>();

if (builder.Configuration.GetValue<bool>("EnableGameServer", true))
{
    // Hosted services start in registration order and stop in reverse order, so the
    // record writer remains available while the network server is shutting down.
    builder.Services.AddHostedService<GameNetworkServer>(); // Add Custom raw TCP/UDP server
}

builder.Services.AddSingleton<BotIdentityService>();
builder.Services.AddSingleton<ActiveGameParticipantRegistry>();
builder.Services.AddSingleton<GameRoomService>();
builder.Services.AddOptions<LiveKitOptions>()
    .Bind(builder.Configuration.GetSection(LiveKitOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) && uri.Scheme == "wss",
        "LiveKit:Url must be an absolute wss:// URL.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                   !string.IsNullOrWhiteSpace(options.ApiSecret),
        "LiveKit API key and API secret are required.")
    .ValidateOnStart();
builder.Services.AddSingleton<LiveKitTokenService>();
builder.Services.AddSingleton<LiveKitRoomAdminService>();

var app = builder.Build();
using (var scope = app.Services.CreateAsyncScope())
{
    var cosmosService = scope.ServiceProvider.GetRequiredService<UserDbService>();
    await cosmosService.InitializeAsync();
    var gameRecordDbService = scope.ServiceProvider.GetRequiredService<GameRecordDbService>();
    await gameRecordDbService.InitializeAsync();
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
