using TabletopCore.Engine.Actions;
using TabletopCore.Engine.Events;

namespace TabletopCore.Engine;

/// <summary>
/// The single source of truth, owned by the server. Mutable, but only the
/// action pipeline mutates it: every gameplay change goes through
/// <see cref="Apply"/>, which validates, mutates, and appends typed events
/// to the append-only log. Replay = same deterministic setup + same seed +
/// same action sequence.
/// </summary>
public sealed class GameState
{
    private readonly Dictionary<PlayerId, Player> _players = [];
    private readonly Dictionary<ZoneId, Zone> _zones = [];
    private readonly Dictionary<CardInstanceId, CardInstance> _cards = [];
    private readonly Dictionary<CardDefinitionId, CardDefinition> _definitions = [];
    private readonly List<GameEvent> _eventLog = [];
    private int _nextCardId = 1;
    private int _nextSeat;

    public GameState(ulong seed, ulong rngSequence = 0)
    {
        Rng = new Pcg32(seed, rngSequence);
    }

    public Pcg32 Rng { get; }
    public TurnState Turn { get; } = new();
    public ITurnSystem TurnSystem { get; set; } = new RoundRobinTurnSystem();

    public IReadOnlyDictionary<PlayerId, Player> Players => _players;
    public IReadOnlyDictionary<ZoneId, Zone> Zones => _zones;
    public IReadOnlyDictionary<CardInstanceId, CardInstance> Cards => _cards;
    public IReadOnlyDictionary<CardDefinitionId, CardDefinition> Definitions => _definitions;
    public IReadOnlyList<GameEvent> EventLog => _eventLog;

    // --- Setup (game classes call these before play; must be deterministic) ---

    public void AddDefinition(CardDefinition definition) => _definitions.Add(definition.Id, definition);

    public Player AddPlayer(string displayName)
    {
        var player = new Player(new PlayerId(_nextSeat), displayName, _nextSeat);
        _players.Add(player.Id, player);
        if (_players.Count == 1)
            Turn.ActivePlayer = player.Id;
        _nextSeat++;
        return player;
    }

    public Zone AddZone(ZoneId id, ZoneKind kind, ZoneVisibility visibility, PlayerId? owner = null)
    {
        if (owner is { } o && !_players.ContainsKey(o))
            throw new ArgumentException($"Unknown owner '{o}' for zone '{id}'.", nameof(owner));
        var zone = new Zone(id, kind, visibility, owner);
        _zones.Add(id, zone);
        return zone;
    }

    public CardInstance CreateCard(CardDefinitionId definition, ZoneId zone, bool faceUp = false, PlayerId? owner = null)
    {
        if (!_definitions.ContainsKey(definition))
            throw new ArgumentException($"Unknown card definition '{definition}'.", nameof(definition));
        var target = GetZone(zone);
        var card = new CardInstance(new CardInstanceId(_nextCardId++), definition, zone, faceUp, owner);
        _cards.Add(card.Id, card);
        target.CardsInternal.Add(card.Id);
        return card;
    }

    // --- Lookups ---

    public Player GetPlayer(PlayerId id)
        => _players.TryGetValue(id, out var player) ? player : throw new KeyNotFoundException($"Unknown player '{id}'.");

    public Zone GetZone(ZoneId id)
        => _zones.TryGetValue(id, out var zone) ? zone : throw new KeyNotFoundException($"Unknown zone '{id}'.");

    public CardInstance GetCard(CardInstanceId id)
        => _cards.TryGetValue(id, out var card) ? card : throw new KeyNotFoundException($"Unknown card '{id}'.");

    public CardDefinition GetDefinition(CardDefinitionId id)
        => _definitions.TryGetValue(id, out var def) ? def : throw new KeyNotFoundException($"Unknown definition '{id}'.");

    /// <summary>
    /// The query indirection (L14): rules read card properties through here,
    /// never off the definition directly, so a future modifier/buff layer can
    /// intercept without rewriting every rule. Absent properties come back as
    /// <see cref="PropertyValue.None"/>.
    /// </summary>
    public PropertyValue GetValue(CardInstanceId card, string property)
    {
        var definition = GetDefinition(GetCard(card).Definition);
        return definition.Properties.TryGetValue(property, out var value) ? value : PropertyValue.None;
    }

    // --- The action pipeline ---

    public ActionResult Apply(GameAction action)
    {
        if (!_players.ContainsKey(action.Actor))
            return ActionResult.Rejected($"Unknown player '{action.Actor}'.");
        var error = action.Validate(this);
        if (error is not null)
            return ActionResult.Rejected(error);

        var events = new List<GameEvent>();
        action.Apply(this, events);
        _eventLog.AddRange(events);
        return ActionResult.Applied(events);
    }

    internal void MoveCard(CardInstance card, Zone to, int? position)
    {
        GetZone(card.Zone).CardsInternal.Remove(card.Id);
        if (position is { } p)
            to.CardsInternal.Insert(Math.Clamp(p, 0, to.CardsInternal.Count), card.Id);
        else
            to.CardsInternal.Add(card.Id);
        card.Zone = to.Id;
    }
}
