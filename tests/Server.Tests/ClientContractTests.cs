using System.Text.RegularExpressions;
using TabletopCore.Protocol;
using Xunit;

namespace TabletopCore.Server.Tests;

/// <summary>
/// The browser client is hand-written JavaScript mirroring C# constants, so
/// nothing but a test keeps the two honest. These read the shipped files as
/// text — crude, but it catches the failure that actually happened: a wire
/// change landing on one side only, which shows up to a player as controls
/// that snap back rather than as an error.
/// </summary>
public class ClientContractTests
{
    private static string ClientFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TabletopCore.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "Server", "wwwroot", relativePath));
    }

    [Fact]
    public void TheBrowserClientDeclaresTheSameProtocolVersionAsTheServer()
    {
        var declared = Regex.Match(ClientFile(Path.Combine("js", "core", "protocol.js")),
            @"PROTOCOL_VERSION\s*=\s*(\d+)");

        Assert.True(declared.Success, "protocol.js no longer declares PROTOCOL_VERSION");
        Assert.Equal(Wire.ProtocolVersion, int.Parse(declared.Groups[1].Value));
    }

    [Theory]
    [InlineData(Wire.Welcome)]
    [InlineData(Wire.Lobby)]
    [InlineData(Wire.Catalog)]
    [InlineData(Wire.State)]
    [InlineData(Wire.MoveAccepted)]
    [InlineData(Wire.MoveRejected)]
    [InlineData(Wire.PlayerStatus)]
    [InlineData(Wire.Error)]
    public void TheBrowserClientSubscribesToEveryMessageTheServerPushes(string method)
    {
        Assert.Contains($"\"{method}\"", ClientFile(Path.Combine("js", "core", "protocol.js")), StringComparison.Ordinal);
    }
}
