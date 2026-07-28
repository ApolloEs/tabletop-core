namespace TabletopCore.Engine;

/// <summary>
/// A participant in the game. Connection details live in the server layer,
/// never here.
/// </summary>
public sealed record Player(PlayerId Id, string DisplayName, int Seat);
