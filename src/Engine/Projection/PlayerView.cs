namespace TabletopCore.Engine.Projection;

/// <summary>
/// What one player is allowed to see (L3). This — never GameState — is what
/// the server serializes to a client, and it doubles as the join/reconnect
/// snapshot. Full hands and hidden zones never leave the server for the
/// wrong client.
/// </summary>
public sealed record PlayerView(
    PlayerId Viewer,
    IReadOnlyList<PlayerSummary> Players,
    IReadOnlyList<ZoneView> Zones,
    TurnInfo Turn);

public sealed record PlayerSummary(PlayerId Id, string DisplayName, int Seat);

/// <summary>Cards is null when the viewer may not see the zone's contents —
/// they get the count only.</summary>
public sealed record ZoneView(
    ZoneId Id,
    ZoneKind Kind,
    ZoneVisibility Visibility,
    PlayerId? Owner,
    int CardCount,
    IReadOnlyList<CardView>? Cards,
    IReadOnlyDictionary<string, int> Counters);

/// <summary>Definition is null when the card is present but its identity is
/// hidden from the viewer (a face-down card in a public zone).</summary>
public sealed record CardView(CardInstanceId Id, CardDefinitionId? Definition, bool FaceUp);

public sealed record TurnInfo(PlayerId ActivePlayer, TurnDirection Direction, int TurnNumber);
