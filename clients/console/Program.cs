using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TabletopCore.ConsoleClient;
using TabletopCore.Engine;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;

// Usage:
//   dotnet run -- create Ava
//   dotnet run -- join KWXZ Bo
//   dotnet run -- resume <token>
//   options: --server http://host:port   (default http://localhost:5199)

string server = "http://localhost:5199";
var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--server" && i + 1 < args.Length) server = args[++i];
    else positional.Add(args[i]);
}
if (positional.Count == 0)
{
    Console.WriteLine("usage: create <name> | join <CODE> <name> | resume <token>   [--server url]");
    return 1;
}

var gate = new object();
string? token = null;
StatePayload? latest = null;
LobbyPayload? lobby = null;
bool amHost = false;
var catalog = new Dictionary<CardDefinitionId, CardCatalogEntry>();
MovePayload? inFlight = null;
long nextMoveId = 1;
var config = new AgonyConfig();

var connection = new HubConnectionBuilder()
    .WithUrl($"{server}/game")
    .WithAutomaticReconnect()
    .AddJsonProtocol(o => o.PayloadSerializerOptions = WireJson.Options)
    .Build();

void Print(string text) { lock (gate) Console.WriteLine(text); }

connection.On<WelcomePayload>(Wire.Welcome, w =>
{
    lock (gate)
    {
        token = w.SessionToken;
        amHost = w.Seat == 0;
        // Resume the move-id sequence where this seat left off — a restarted
        // process starting back at 1 would collide with the server's dedupe.
        nextMoveId = Math.Max(nextMoveId, w.LastMoveId + 1);
    }
    Print($"\n== room {w.RoomCode} — you are seat {w.Seat} ==");
    Print($"   resume token (save this): {w.SessionToken}");
});

connection.On<LobbyPayload>(Wire.Lobby, p =>
{
    lock (gate) { lobby = p; config = p.Config; }
    var seats = p.Players.Select(x =>
        $"{x.Name}{(x.IsHost ? "*" : "")}{(x.Connected ? "" : " (away)")}");
    Print($"\n[lobby] {string.Join(", ", seats)}   stacking:{OnOff(p.Config.StackDrawCards)}  swap/rotate:{OnOff(p.Config.SwapRotateCards)}  placings:{OnOff(p.Config.PlayForPlacings)}");
    if (amHost)
        Print(p.CanStart
            ? "type: start | stack | swap | placings"
            : "waiting for players… (type: stack | swap | placings)");
});

connection.On<CardCatalog>(Wire.Catalog, c =>
{
    lock (gate)
    {
        catalog.Clear();
        foreach (var entry in c.Definitions) catalog[entry.Id] = entry;
    }
});

connection.On<StatePayload>(Wire.State, s =>
{
    lock (gate) { if (latest is null || s.Version >= latest.Version) latest = s; else return; }
    Print(GameView.Render(s, catalog));
});

connection.On<MoveAcceptedPayload>(Wire.MoveAccepted, a =>
{
    lock (gate) { if (inFlight?.MoveId == a.MoveId) inFlight = null; }
});

connection.On<MoveRejectedPayload>(Wire.MoveRejected, r =>
{
    lock (gate) { if (inFlight?.MoveId == r.MoveId) inFlight = null; }
    Print($"  rejected: {r.Error}");
});

connection.On<PlayerStatusPayload>(Wire.PlayerStatus, s =>
{
    string what = s.Connected ? "is back" : s.Abandoned ? "abandoned the game (host may 'end')" : "lost connection";
    Print($"  · seat {s.Seat} {what}");
});

connection.On<ErrorPayload>(Wire.Error, e => Print($"  server: [{e.Code}] {e.Message}"));

connection.Reconnecting += _ => { Print("  … connection lost, reconnecting"); return Task.CompletedTask; };
connection.Reconnected += async _ =>
{
    Print("  … pipe is back, resuming seat");
    string? t; MovePayload? resend;
    lock (gate) { t = token; resend = inFlight; }
    if (t is not null) await connection.InvokeAsync("resume", t);
    // The one-in-flight rule: whatever was unacked when we dropped is
    // resent verbatim; the server dedupes by move id, so this is safe.
    if (resend is not null) await connection.InvokeAsync("submitMove", resend);
};
connection.Closed += _ => { Print("  connection closed for good — restart with 'resume <token>'."); return Task.CompletedTask; };

await connection.StartAsync();

await (positional[0] switch
{
    "create" => connection.InvokeAsync("createRoom", At(positional, 1) ?? "Host", "agony"),
    "join" => connection.InvokeAsync("joinRoom", At(positional, 1) ?? "", At(positional, 2) ?? "Guest"),
    "resume" => connection.InvokeAsync("resume", At(positional, 1) ?? ""),
    _ => Task.FromException(new ArgumentException($"unknown verb '{positional[0]}'")),
});

while (Console.ReadLine() is { } line)
{
    line = line.Trim().ToLowerInvariant();
    try
    {
        switch (line)
        {
            case "quit" or "q":
                await connection.StopAsync();
                return 0;
            case "start":
                await connection.InvokeAsync("start");
                break;
            case "end":
                await connection.InvokeAsync("endGame");
                break;
            case "again":
                await connection.InvokeAsync("rematch");
                break;
            case "lobby":
                await connection.InvokeAsync("backToLobby");
                break;
            case "stack":
                await connection.InvokeAsync("setConfig", config with { StackDrawCards = !config.StackDrawCards });
                break;
            case "swap":
                await connection.InvokeAsync("setConfig", config with { SwapRotateCards = !config.SwapRotateCards });
                break;
            case "placings":
                await connection.InvokeAsync("setConfig", config with { PlayForPlacings = !config.PlayForPlacings });
                break;
            case var _ when int.TryParse(line, out int pick):
                StatePayload? state; MovePayload? pending;
                lock (gate) { state = latest; pending = inFlight; }
                if (state is null || state.LegalMoves.Count == 0)
                    { Print("  not your turn"); break; }
                if (pick < 1 || pick > state.LegalMoves.Count)
                    { Print($"  no such move (1–{state.LegalMoves.Count})"); break; }
                if (pending is not null)
                    { Print("  still waiting for the last move's ack"); break; }
                MovePayload payload;
                lock (gate) payload = inFlight = new MovePayload(nextMoveId++, state.LegalMoves[pick - 1]);
                await connection.InvokeAsync("submitMove", payload);
                break;
            case "":
                break;
            default:
                Print("  commands: <move number> | start | stack | swap | placings | end | again | lobby | quit");
                break;
        }
    }
    catch (Exception ex)
    {
        Print($"  ! {ex.Message}");
    }
}
return 0;

static string? At(List<string> list, int index) => index < list.Count ? list[index] : null;
static string OnOff(bool value) => value ? "ON" : "off";
