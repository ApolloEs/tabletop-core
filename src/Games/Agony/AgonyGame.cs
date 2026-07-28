using TabletopCore.Engine;
using TabletopCore.Engine.Actions;

namespace TabletopCore.Games.Agony;

/// <summary>
/// The first concrete game — built as a test harness for the engine's public
/// API, not as a product (SPEC §12). Rules live here in the module; cards are
/// inert data. v1 rule simplifications, all deliberate: the first discard is
/// re-flipped until it is a number card; Wild +4 has no challenge rule and no
/// "only when you cannot match" restriction; emptying your hand wins
/// immediately (a final +2's debt is never collected); after drawing you may
/// play only the drawn card.
/// </summary>
public sealed class AgonyGame : IGame
{
    public static readonly ZoneId DeckZone = new("deck");
    public static readonly ZoneId DiscardZone = new("discard");
    public static readonly ZoneId TableZone = new("table");

    public const string ActiveColorCounter = "activeColor";
    public const string PendingDrawCounter = "pendingDraw";
    public const string HasDrawnCounter = "hasDrawn";
    public const string DrawnCardCounter = "drawnCard";

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
        if (GetWinner(state) is not null)
            return MoveResult.Rejected("The game is over.");
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
            if (!_config.StackDrawTwo)
                return MoveResult.Rejected($"You must draw {pending}.");
            if (symbol != AgonyDeck.SymbolDrawTwo || TopSymbol(state) != AgonyDeck.SymbolDrawTwo)
                return MoveResult.Rejected("Only a +2 can answer a +2.");
        }

        if (table.GetCounter(HasDrawnCounter) == 1 && card.Id.Value != table.GetCounter(DrawnCardCounter))
            return MoveResult.Rejected("After drawing you may only play the drawn card, or pass.");

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
            ApplyCardEffect(state, player, symbol, pending);

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
                Must(state.Apply(new EndTurnAction(player, Skip: state.Players.Count == 2 ? 1 : 0)));
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
            Must(state.Apply(new EndTurnAction(player)));
            return MoveResult.Applied(EventsSince(state, mark));
        }

        if (table.GetCounter(HasDrawnCounter) == 1)
            return MoveResult.Rejected("You already drew this turn — play the drawn card or pass.");

        RecycleDiscardIfNeeded(state, player, 1);
        if (state.GetZone(DeckZone).Count == 0)
            return MoveResult.Rejected("There is nothing left to draw — play a card or pass.");

        Must(state.Apply(new DrawAction(player, DeckZone, HandOf(state, player), 1)));
        var drawn = state.GetZone(HandOf(state, player)).Cards[^1];
        Must(state.Apply(new SetCounterAction(player, TableZone, HasDrawnCounter, 1)));
        Must(state.Apply(new SetCounterAction(player, TableZone, DrawnCardCounter, drawn.Value)));
        return MoveResult.Applied(EventsSince(state, mark));
    }

    private static MoveResult TryPass(GameState state, PlayerId player)
    {
        var table = state.GetZone(TableZone);
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
        if (GetWinner(state) is not null || state.Turn.ActivePlayer != player)
            return [];

        var table = state.GetZone(TableZone);
        var hand = state.GetZone(HandOf(state, player)).Cards;
        var moves = new List<GameMove>();

        if (table.GetCounter(PendingDrawCounter) > 0)
        {
            moves.Add(new DrawCard());
            if (_config.StackDrawTwo && TopSymbol(state) == AgonyDeck.SymbolDrawTwo)
                moves.AddRange(hand
                    .Where(id => state.GetValue(id, "symbol").AsString == AgonyDeck.SymbolDrawTwo)
                    .Select(id => new PlayCard(id)));
            return moves;
        }

        if (table.GetCounter(HasDrawnCounter) == 1)
        {
            var drawn = new CardInstanceId(table.GetCounter(DrawnCardCounter));
            if (hand.Contains(drawn) && IsPlayable(state, drawn))
                moves.AddRange(PlayVariants(state, drawn));
            moves.Add(new PassTurn());
            return moves;
        }

        moves.AddRange(hand.Where(id => IsPlayable(state, id)).SelectMany(id => PlayVariants(state, id)));
        bool canDraw = state.GetZone(DeckZone).Count > 0 || state.GetZone(DiscardZone).Count > 1;
        moves.Add(canDraw ? new DrawCard() : new PassTurn());
        return moves;
    }

    public PlayerId? GetWinner(GameState state)
    {
        foreach (var player in state.Players.Values.OrderBy(p => p.Seat))
        {
            if (state.Zones.TryGetValue(HandOf(state, player.Id), out var hand) && hand.Count == 0)
                return player.Id;
        }
        return null;
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

    private static IEnumerable<PlayCard> PlayVariants(GameState state, CardInstanceId card)
        => state.GetValue(card, "color").AsString == AgonyDeck.WildColor
            ? Enum.GetValues<AgonyColor>().Select(c => new PlayCard(card, c))
            : [new PlayCard(card)];

    private static string TopSymbol(GameState state)
        => state.GetValue(state.GetZone(DiscardZone).Cards[^1], "symbol").AsString;

    // --- Expansion helpers (public engine actions only) ---

    private static void ClearDrawFlags(GameState state, PlayerId player, Zone table)
    {
        if (table.GetCounter(HasDrawnCounter) != 0)
        {
            Must(state.Apply(new SetCounterAction(player, TableZone, HasDrawnCounter, 0)));
            Must(state.Apply(new SetCounterAction(player, TableZone, DrawnCardCounter, 0)));
        }
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
        var players = state.Players.Values.OrderBy(p => p.Seat).Select(p => p.Id).ToList();
        var snapshots = players.ToDictionary(p => p, p => state.GetZone(HandOf(state, p)).Cards.ToList());
        foreach (var player in players)
        {
            var target = NextBySeat(state, player);
            foreach (var id in snapshots[player])
                Must(state.Apply(new MoveCardAction(actor, id, HandOf(state, target))));
        }
    }

    private static PlayerId NextBySeat(GameState state, PlayerId player)
    {
        var players = state.Players.Values.OrderBy(p => p.Seat).ToList();
        int index = players.FindIndex(p => p.Id == player);
        int step = (int)state.Turn.Direction;
        return players[((index + step) % players.Count + players.Count) % players.Count].Id;
    }

    private static IReadOnlyList<Engine.Events.GameEvent> EventsSince(GameState state, int mark)
        => state.EventLog.Skip(mark).ToList();

    private static void Must(ActionResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException($"Engine rejected an expanded action: {result.Error}");
    }
}
