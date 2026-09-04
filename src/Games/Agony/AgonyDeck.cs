using TabletopCore.Engine;

namespace TabletopCore.Games.Agony;

/// <summary>
/// The standard Agony deck, generated programmatically: per color one 0, two
/// each of 1–9 / Skip / Reverse / +2 (and one Swap + one Rotate when the
/// house rule is on), plus four Wilds and four Wild +4s. Custom decks replace
/// this via the content pipeline (cards.json) in a later phase; the shape of
/// the properties ("color", "symbol", "number") is the contract they follow.
/// </summary>
public static class AgonyDeck
{
    public const string SymbolSkip = "skip";
    public const string SymbolReverse = "reverse";
    public const string SymbolDrawTwo = "draw2";
    public const string SymbolSwap = "swap";
    public const string SymbolRotate = "rotate";
    public const string SymbolWild = "wild";
    public const string SymbolWildFour = "wild4";
    public const string WildColor = "wild";

    public static void Populate(GameState state, ZoneId deck, bool includeSwapRotate)
    {
        var pending = new List<CardDefinitionId>();

        foreach (var color in Enum.GetValues<AgonyColor>())
        {
            string c = AgonyColors.Name(color);
            string display = char.ToUpperInvariant(c[0]) + c[1..];

            for (int n = 0; n <= 9; n++)
                Add(state, pending, $"{c}-{n}", $"{display} {n}", copies: n == 0 ? 1 : 2, c, n.ToString(), n);

            Add(state, pending, $"{c}-skip", $"{display} Skip", 2, c, SymbolSkip);
            Add(state, pending, $"{c}-reverse", $"{display} Reverse", 2, c, SymbolReverse);
            Add(state, pending, $"{c}-draw2", $"{display} +2", 2, c, SymbolDrawTwo);

            if (includeSwapRotate)
            {
                Add(state, pending, $"{c}-swap", $"{display} Swap", 1, c, SymbolSwap);
                Add(state, pending, $"{c}-rotate", $"{display} Rotate", 1, c, SymbolRotate);
            }
        }

        Add(state, pending, "wild", "Wild", 4, WildColor, SymbolWild);
        Add(state, pending, "wild4", "Wild +4", 4, WildColor, SymbolWildFour);

        // Instances are created in seeded-random order, not generation order.
        // A CardInstanceId's binding to its definition is decided at creation,
        // so a fixed creation order would make ids decodable by anyone with
        // this (public) source — every exposed id of a hidden card would leak
        // the exact card. Shuffling here makes ids meaningless per game while
        // staying deterministic per seed (replay-safe).
        for (int i = pending.Count - 1; i > 0; i--)
        {
            int j = state.Rng.NextInt(i + 1);
            (pending[i], pending[j]) = (pending[j], pending[i]);
        }
        foreach (var definition in pending)
            state.CreateCard(definition, deck);
    }

    private static void Add(
        GameState state, List<CardDefinitionId> pending, string id, string name, int copies,
        string color, string symbol, int? number = null)
    {
        var properties = new Dictionary<string, PropertyValue>
        {
            ["color"] = color,
            ["symbol"] = symbol,
        };
        if (number is { } n)
            properties["number"] = n;

        var definitionId = new CardDefinitionId(id);
        state.AddDefinition(new CardDefinition(definitionId, name, properties: properties));
        for (int i = 0; i < copies; i++)
            pending.Add(definitionId);
    }
}
