namespace TabletopCore.Engine;

public enum ZoneKind
{
    Pile,
    Hand,
    Spread,
    /// <summary>Spatial zone for board games. Cell/token structure arrives
    /// with game #2 (the football game) — extracted, not pre-designed (L5).</summary>
    Grid,
    Counter,
}

public enum ZoneVisibility
{
    /// <summary>Contents visible to all (face-down cards still hide their identity).</summary>
    Public,
    /// <summary>Contents visible only to the owning player (a hand).</summary>
    OwnerOnly,
    /// <summary>Contents visible to no client — count only (a deck).</summary>
    Hidden,
}

/// <summary>
/// A general container the game populates (L4): an ordered card list plus
/// named counters. Deck, hand, discard, a poker pot, a board square — all
/// zones with different settings. Card ordering: index 0 is the bottom,
/// the last index is the top (draws come off the top).
/// </summary>
public sealed class Zone
{
    private readonly List<CardInstanceId> _cards = [];
    private readonly Dictionary<string, int> _counters = [];

    internal Zone(ZoneId id, ZoneKind kind, ZoneVisibility visibility, PlayerId? owner)
    {
        Id = id;
        Kind = kind;
        Visibility = visibility;
        Owner = owner;
    }

    public ZoneId Id { get; }
    public ZoneKind Kind { get; }
    public ZoneVisibility Visibility { get; }
    public PlayerId? Owner { get; }

    public IReadOnlyList<CardInstanceId> Cards => _cards;
    public int Count => _cards.Count;
    public IReadOnlyDictionary<string, int> Counters => _counters;

    public int GetCounter(string name) => _counters.TryGetValue(name, out var value) ? value : 0;

    internal List<CardInstanceId> CardsInternal => _cards;
    internal void SetCounter(string name, int value) => _counters[name] = value;
}
