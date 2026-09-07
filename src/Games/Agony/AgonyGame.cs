using TabletopCore.Engine;
using TabletopCore.Engine.Actions;

namespace TabletopCore.Games.Agony;

/// <summary>
/// The first concrete game — built as a test harness for the engine's public
/// API, not as a product (SPEC §12). Rules live here in the module; cards are
/// inert data. v1 rule simplifications, all deliberate: the first discard is
/// re-flipped until it is a number card; Wild +4 has no challenge rule and no
/// "only when you cannot match" restriction; going out with an action card
/// does not apply that card's effect (a final +2 collects no debt); after
/// drawing you may play any playable card — the drawn one included — or pass
/// (you just can't draw twice).
/// </summary>
public sealed class AgonyGame : IGame
{
    public static readonly ZoneId DeckZone = new("deck");
    public static readonly ZoneId DiscardZone = new("discard");
    public static readonly ZoneId TableZone = new("table");

    // Table counters are PUBLIC — everything here is knowledge every player
    // legitimately has (they all saw the draw happen, the color declared,
    // the debt accumulate, who went out). Nothing hand-private may ever live
    // on the table: a "which card was drawn" counter briefly did, and let
    // opponents tell "played the drawn card" from "played a card held all
    // along".
    public const string ActiveColorCounter = "activeColor";
    public const string PendingDrawCounter = "pendingDraw";
    public const string HasDrawnCounter = "hasDrawn";
    public const string FinishedCountCounter = "finishedCount";

    /// <summary>Counter naming the seat that took a given placing. Seats are
    /// stored +1 because an unset counter reads as 0, which would otherwise
    /// be indistinguishable from seat 0.</summary>
    public static string FinishedSeatCounter(int placing) => $"finished:{placing}";

    private readonly AgonyConfig _config;

    public AgonyGame(AgonyConfig? config = null)
    {
        _config = config ?? new AgonyConfig();
        if (_config.JumpIn)
            throw new NotSupportedException("Jump-in is not implemented yet; it arrives with the wire client.");
    }

    public string Name => "Agony";

    public static ZoneId HandOf(GameState state, PlayerId player) => new($"hand:{state.GetPlayer(player).Seat}");

    // --- Setup ---

    public void Setup(GameState state)
    {
        if (state.Players.Count is < 2 or > 10)
            throw new ArgumentException($"Agony needs 2–10 players, got {state.Players.Count}.");

        state.AddZone(DeckZone, ZoneKind.Pile, ZoneVisibility.Hidden);
        state.AddZone(DiscardZone, ZoneKind.Pile, ZoneVisibility.Public);
        state.AddZone(TableZone, ZoneKind.Counter, ZoneVisibility.Public);
        foreach (var player in state.Players.Values.OrderBy(p => p.Seat))
            state.AddZone(HandOf(state, player.Id), ZoneKind.Hand, ZoneVisibility.OwnerOnly, player.Id);

        // Players who have gone out must be stepped over once the game can
        // outlive the first empty hand.
        state.TurnSystem = new AgonyTurnSystem();

        AgonyDeck.Populate(state, DeckZone, _config.SwapRotateCards);

        var dealer = state.Players.Values.OrderBy(p => p.Seat).First().Id;
        Must(state.Apply(new ShuffleAction(dealer, DeckZone)));
        foreach (var player in state.Players.Values.OrderBy(p => p.Seat))
            Must(state.Apply(new DrawAction(player.Id, DeckZone, HandOf(state, player.Id), 7)));

        // Flip the starting discard; anything but a number card goes back in.
        while (true)
        {
            var top = state.GetZone(DeckZone).Cards[^1];
            Must(state.Apply(new MoveCardAction(dealer, top, DiscardZone)));
            if (!state.GetValue(top, "number").IsNone)
            {
                Must(state.Apply(new FlipCardAction(dealer, top, FaceUp: true)));
                var color = AgonyColors.Parse(state.GetValue(top, "color").AsString);
                Must(state.Apply(new SetCounterAction(dealer, TableZone, ActiveColorCounter, (int)color)));
                break;
            }
            Must(state.Apply(new MoveCardAction(dealer, top, DeckZone)));
            Must(state.Apply(new ShuffleAction(dealer, DeckZone)));
        }
    }

    // --- Moves ---

    public MoveResult TryMove(GameState state, PlayerId player, GameMove move)
    {
        if (IsFinished(state))
            return MoveResult.Rejected("The game is over.");
        if (HasGoneOut(state, player))
            return MoveResult.Rejected("You are out — your placing is already settled.");
        if (state.Turn.ActivePlayer != player)
            return MoveResult.Rejected($"It is not {player}'s turn.");

        return move switch
        {
            PlayCard play => TryPlay(state, player, play),
            DrawCard => TryDraw(state, player),
            PassTurn => TryPass(state, player),
            _ => MoveResult.Rejected($"'{move.GetType().Name}' is not an Agony move."),
        };
    }

    private MoveResult TryPlay(GameState state, PlayerId player, PlayCard move)
    {
        if (!state.Cards.ContainsKey(move.Card))
            return MoveResult.Rejected($"Unknown card '{move.Card}'.");
        var card = state.GetCard(move.Card);
        if (card.Zone != HandOf(state, player))
            return MoveResult.Rejected("That card is not in your hand.");

        var table = state.GetZone(TableZone);
        string symbol = state.GetValue(card.Id, "symbol").AsString;
        bool isWild = state.GetValue(card.Id, "color").AsString == AgonyDeck.WildColor;
        int pending = table.GetCounter(PendingDrawCounter);

        if (pending > 0)
        {
            if (!_config.StackDrawCards)
                return MoveResult.Rejected($"You must take the {pending} card(s).");
            if (!CanAnswerPendingDraw(state, symbol))
                return MoveResult.Rejected(TopSymbol(state) == AgonyDeck.SymbolWildFour
                    ? "Only a Wild +4 can answer a Wild +4."
                    : "Only a +2 or a Wild +4 can answer a +2.");
        }

        // Having drawn does not narrow what you may play: any playable card
        // (the drawn one included) is fine. The server never says which card
        // was drawn — the drawer's own client can tell by diffing its hand.

        if (isWild)
        {
            if (move.DeclaredColor is null)
                return MoveResult.Rejected("A wild card requires a declared color.");
        }
        else
        {
            if (move.DeclaredColor is not null)
                return MoveResult.Rejected("Only wild cards declare a color.");
            if (!IsPlayable(state, card.Id))
                return MoveResult.Rejected("That card matches neither the active color nor the top card.");
        }

        int mark = state.EventLog.Count;

        Must(state.Apply(new MoveCardAction(player, card.Id, DiscardZone)));
        if (!card.FaceUp)
            Must(state.Apply(new FlipCardAction(player, card.Id, FaceUp: true)));

        var newColor = isWild
            ? move.DeclaredColor!.Value
            : AgonyColors.Parse(state.GetValue(card.Id, "color").AsString);
        Must(state.Apply(new SetCounterAction(player, TableZone, ActiveColorCounter, (int)newColor)));
        ClearDrawFlags(state, player, table);

        if (state.GetZone(HandOf(state, player)).Count > 0)
        {
            ApplyCardEffect(state, player, symbol, pending);
        }
        else
        {
            // Going out takes the next placing. Playing for placings, the
            // rest carry on without this seat — the turn system steps over
            // empty hands, so ending the turn hands play to the next player
            // still holding cards.
            RecordFinished(state, player);
            if (!IsFinished(state))
                Must(state.Apply(new EndTurnAction(player)));
        }

        return MoveResult.Applied(EventsSince(state, mark));
    }

    private void ApplyCardEffect(GameState state, PlayerId player, string symbol, int pending)
    {
        switch (symbol)
        {
            case AgonyDeck.SymbolSkip:
                Must(state.Apply(new EndTurnAction(player, Skip: 1)));
                break;
            case AgonyDeck.SymbolReverse:
                var flipped = state.Turn.Direction == TurnDirection.Forward
                    ? TurnDirection.Reversed
                    : TurnDirection.Forward;
                Must(state.Apply(new SetTurnDirectionAction(player, flipped)));
                // With two players, reverse comes straight back around: a skip.
                Must(state.Apply(new EndTurnAction(player, Skip: ActiveCount(state) == 2 ? 1 : 0)));
                break;
            case AgonyDeck.SymbolDrawTwo:
                Must(state.Apply(new SetCounterAction(player, TableZone, PendingDrawCounter, pending + 2)));
                Must(state.Apply(new EndTurnAction(player)));
                break;
            case AgonyDeck.SymbolWildFour:
                Must(state.Apply(new SetCounterAction(player, TableZone, PendingDrawCounter, pending + 4)));
                Must(state.Apply(new EndTurnAction(player)));
                break;
            case AgonyDeck.SymbolSwap:
                SwapHands(state, player, NextBySeat(state, player));
                Must(state.Apply(new EndTurnAction(player)));
                break;
            case AgonyDeck.SymbolRotate:
                RotateHands(state, player);
                Must(state.Apply(new EndTurnAction(player)));
                break;
            default: // numerals, plain wild
                Must(state.Apply(new EndTurnAction(player)));
                break;
        }
    }

    private static MoveResult TryDraw(GameState state, PlayerId player)
    {
        var table = state.GetZone(TableZone);
        int pending = table.GetCounter(PendingDrawCounter);
        int mark = state.EventLog.Count;

        if (pending > 0)
        {
            RecycleDiscardIfNeeded(state, player, pending);
            int available = Math.Min(pending, state.GetZone(DeckZone).Count);
            if (available > 0)
                Must(state.Apply(new DrawAction(player, DeckZone, HandOf(state, player), available)));
            Must(state.Apply(new SetCounterAction(player, TableZone, PendingDrawCounter, 0)));
            // Collecting does NOT end the turn — the player carries on with a
            // normal turn: play something now, or draw one and pass. Their
            // one normal draw is still untouched, so hasDrawn stays clear.
            return MoveResult.Applied(EventsSince(state, mark));
        }

        if (table.GetCounter(HasDrawnCounter) == 1)
            return MoveResult.Rejected("You already drew this turn — play a card or pass.");

        RecycleDiscardIfNeeded(state, player, 1);
        if (state.GetZone(DeckZone).Count == 0)
            return MoveResult.Rejected("There is nothing left to draw — play a card or pass.");

        Must(state.Apply(new DrawAction(player, DeckZone, HandOf(state, player), 1)));
        Must(state.Apply(new SetCounterAction(player, TableZone, HasDrawnCounter, 1)));
        // Deliberately NO end-of-turn here, and no automatic pass anywhere:
        // an auto-pass that fires when nothing is playable is a 1-bit oracle
        // on the hand (firing = "has nothing", not firing = "saving
        // something"). Passing is always an explicit move, so the time it
        // takes carries only human noise.
        return MoveResult.Applied(EventsSince(state, mark));
    }

    private static MoveResult TryPass(GameState state, PlayerId player)
    {
        var table = state.GetZone(TableZone);
        if (table.GetCounter(PendingDrawCounter) > 0)
            return MoveResult.Rejected("Answer or take the pending draw first.");

        bool nothingToDraw = state.GetZone(DeckZone).Count == 0 && state.GetZone(DiscardZone).Count <= 1;
        if (table.GetCounter(HasDrawnCounter) != 1 && !nothingToDraw)
            return MoveResult.Rejected("You must draw (or play) before passing.");

        int mark = state.EventLog.Count;
        ClearDrawFlags(state, player, table);
        Must(state.Apply(new EndTurnAction(player)));
        return MoveResult.Applied(EventsSince(state, mark));
    }

    // --- Queries ---

    public IReadOnlyList<GameMove> GetLegalMoves(GameState state, PlayerId player)
    {
        if (IsFinished(state) || HasGoneOut(state, player) || state.Turn.ActivePlayer != player)
            return [];

        var table = state.GetZone(TableZone);
        var hand = state.GetZone(HandOf(state, player)).Cards;
        var moves = new List<GameMove>();

        if (table.GetCounter(PendingDrawCounter) > 0)
        {
            // Taking the debt is always available (the client renders this
            // as "take +N"); a *normal* draw is not on offer until the debt
            // is settled. Never auto-taken, even with nothing to answer with
            // — being forced is still the player's own click.
            moves.Add(new DrawCard());
            if (_config.StackDrawCards)
                moves.AddRange(hand
                    .Where(id => CanAnswerPendingDraw(state, state.GetValue(id, "symbol").AsString))
                    .SelectMany(id => PlayVariants(state, id)));
            return moves;
        }

        if (table.GetCounter(HasDrawnCounter) == 1)
        {
            // Same play options as before the draw, plus whatever the drawn
            // card added — only the second draw is off the table.
            moves.AddRange(hand.Where(id => IsPlayable(state, id)).SelectMany(id => PlayVariants(state, id)));
            moves.Add(new PassTurn());
            return moves;
        }

        moves.AddRange(hand.Where(id => IsPlayable(state, id)).SelectMany(id => PlayVariants(state, id)));
        bool canDraw = state.GetZone(DeckZone).Count > 0 || state.GetZone(DiscardZone).Count > 1;
        moves.Add(canDraw ? new DrawCard() : new PassTurn());
        return moves;
    }

    /// <summary>Seats in the order they emptied their hands. One entry means
    /// a winner; playing for placings this grows to players − 1.</summary>
    public IReadOnlyList<PlayerId> GetStandings(GameState state)
    {
        if (!state.Zones.TryGetValue(TableZone, out var table))
            return [];

        int finished = table.GetCounter(FinishedCountCounter);
        var standings = new List<PlayerId>(finished);
        for (int placing = 0; placing < finished; placing++)
        {
            int seat = table.GetCounter(FinishedSeatCounter(placing)) - 1; // stored +1
            var finisher = state.Players.Values.FirstOrDefault(p => p.Seat == seat);
            if (finisher is not null)
                standings.Add(finisher.Id);
        }
        return standings;
    }

    public bool IsFinished(GameState state)
    {
        if (!state.Zones.TryGetValue(TableZone, out var table))
            return false;

        int finished = table.GetCounter(FinishedCountCounter);
        return _config.PlayForPlacings
            ? finished >= state.Players.Count - 1  // play on until one is left holding cards
            : finished >= 1;                       // first one out wins, everyone stops
    }

    /// <summary>The base matching rule: wilds always; otherwise active color
    /// or the top card's symbol.</summary>
    private static bool IsPlayable(GameState state, CardInstanceId card)
    {
        string color = state.GetValue(card, "color").AsString;
        if (color == AgonyDeck.WildColor)
            return true;
        var active = (AgonyColor)state.GetZone(TableZone).GetCounter(ActiveColorCounter);
        return color == AgonyColors.Name(active)
            || state.GetValue(card, "symbol").AsString == TopSymbol(state);
    }

    /// <summary>The stacking ladder: a +2 may be answered with a +2 or a
    /// Wild +4, but once a +4 is on the table only another +4 stops it — so
    /// +4 stays the strongest card in the deck.</summary>
    private static bool CanAnswerPendingDraw(GameState state, string symbol) => TopSymbol(state) switch
    {
        AgonyDeck.SymbolWildFour => symbol == AgonyDeck.SymbolWildFour,
        AgonyDeck.SymbolDrawTwo => symbol is AgonyDeck.SymbolDrawTwo or AgonyDeck.SymbolWildFour,
        _ => false,
    };

    private static bool HasGoneOut(GameState state, PlayerId player)
        => state.GetZone(HandOf(state, player)).Count == 0;

    private static IEnumerable<PlayCard> PlayVariants(GameState state, CardInstanceId card)
        => state.GetValue(card, "color").AsString == AgonyDeck.WildColor
            ? Enum.GetValues<AgonyColor>().Select(c => new PlayCard(card, c))
            : [new PlayCard(card)];

    private static string TopSymbol(GameState state)
        => state.GetValue(state.GetZone(DiscardZone).Cards[^1], "symbol").AsString;

    // --- Expansion helpers (public engine actions only) ---

    private static void RecordFinished(GameState state, PlayerId player)
    {
        var table = state.GetZone(TableZone);
        int placing = table.GetCounter(FinishedCountCounter);
        int seat = state.GetPlayer(player).Seat;
        Must(state.Apply(new SetCounterAction(player, TableZone, FinishedSeatCounter(placing), seat + 1)));
        Must(state.Apply(new SetCounterAction(player, TableZone, FinishedCountCounter, placing + 1)));
    }

    private static void ClearDrawFlags(GameState state, PlayerId player, Zone table)
    {
        if (table.GetCounter(HasDrawnCounter) != 0)
            Must(state.Apply(new SetCounterAction(player, TableZone, HasDrawnCounter, 0)));
    }

    private static void RecycleDiscardIfNeeded(GameState state, PlayerId player, int needed)
    {
        var deck = state.GetZone(DeckZone);
        var discard = state.GetZone(DiscardZone);
        if (deck.Count >= needed || discard.Count <= 1)
            return;

        foreach (var id in discard.Cards.Take(discard.Count - 1).ToList())
        {
            Must(state.Apply(new MoveCardAction(player, id, DeckZone)));
            if (state.GetCard(id).FaceUp)
                Must(state.Apply(new FlipCardAction(player, id, FaceUp: false)));
        }
        Must(state.Apply(new ShuffleAction(player, DeckZone)));
    }

    private static void SwapHands(GameState state, PlayerId a, PlayerId b)
    {
        var aCards = state.GetZone(HandOf(state, a)).Cards.ToList();
        var bCards = state.GetZone(HandOf(state, b)).Cards.ToList();
        foreach (var id in aCards)
            Must(state.Apply(new MoveCardAction(a, id, HandOf(state, b))));
        foreach (var id in bCards)
            Must(state.Apply(new MoveCardAction(a, id, HandOf(state, a))));
    }

    private static void RotateHands(GameState state, PlayerId actor)
    {
        // Targets are worked out from a snapshot before anything moves:
        // recomputing mid-rotation would read hands that are already half
        // shuffled around.
        var players = ActiveBySeat(state);
        var snapshots = players.ToDictionary(p => p, p => state.GetZone(HandOf(state, p)).Cards.ToList());
        int step = (int)state.Turn.Direction;
        for (int i = 0; i < players.Count; i++)
        {
            var target = players[((i + step) % players.Count + players.Count) % players.Count];
            foreach (var id in snapshots[players[i]])
                Must(state.Apply(new MoveCardAction(actor, id, HandOf(state, target))));
        }
    }

    private static PlayerId NextBySeat(GameState state, PlayerId player)
    {
        var players = ActiveBySeat(state);
        int index = players.IndexOf(player);
        int step = (int)state.Turn.Direction;
        return players[((index + step) % players.Count + players.Count) % players.Count];
    }

    /// <summary>Seat order restricted to players still holding cards — swap
    /// and rotate must never deal cards back to someone who has gone out.</summary>
    private static List<PlayerId> ActiveBySeat(GameState state)
        => state.Players.Values
            .OrderBy(p => p.Seat)
            .Where(p => state.GetZone(HandOf(state, p.Id)).Count > 0)
            .Select(p => p.Id)
            .ToList();

    private static int ActiveCount(GameState state) => ActiveBySeat(state).Count;

    private static IReadOnlyList<Engine.Events.GameEvent> EventsSince(GameState state, int mark)
        => state.EventLog.Skip(mark).ToList();

    private static void Must(ActionResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException($"Engine rejected an expanded action: {result.Error}");
    }
}
