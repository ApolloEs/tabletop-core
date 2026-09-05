using TabletopCore.Protocol;
using TabletopCore.Server;
using TabletopCore.Server.Rooms;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSignalR(options =>
    {
        // Explicit, not defaults-by-accident: these are wire behavior.
        options.MaximumReceiveMessageSize = 64 * 1024;
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    })
    // The whole point of src/Protocol: SignalR speaks WireJson, so hub
    // payloads are byte-identical to what the golden tests freeze.
    .AddJsonProtocol(options => options.PayloadSerializerOptions = WireJson.Options);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClientSender, SignalRClientSender>();
builder.Services.AddSingleton<RoomRegistry>();

var app = builder.Build();

// Binding comes from env/config (ASPNETCORE_URLS or --urls) — nothing
// hardcoded, so the same binary serves localhost, LAN, or a public box.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/game");

app.Run();

// Exposes the entry point to WebApplicationFactory in Server.Tests.
public partial class Program;
