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
        foreach (var color in Enum.GetValues<AgonyColor>())
        {
            string c = AgonyColors.Name(color);
            string display = char.ToUpperInvariant(c[0]) + c[1..];

            for (int n = 0; n <= 9; n++)
                Add(state, deck, $"{c}-{n}", $"{display} {n}", copies: n == 0 ? 1 : 2, c, n.ToString(), n);

            Add(state, deck, $"{c}-skip", $"{display} Skip", 2, c, SymbolSkip);
            Add(state, deck, $"{c}-reverse", $"{display} Reverse", 2, c, SymbolReverse);
            Add(state, deck, $"{c}-draw2", $"{display} +2", 2, c, SymbolDrawTwo);

            if (includeSwapRotate)
            {
                Add(state, deck, $"{c}-swap", $"{display} Swap", 1, c, SymbolSwap);
                Add(state, deck, $"{c}-rotate", $"{display} Rotate", 1, c, SymbolRotate);
            }
        }

        Add(state, deck, "wild", "Wild", 4, WildColor, SymbolWild);
        Add(state, deck, "wild4", "Wild +4", 4, WildColor, SymbolWildFour);
    }

    private static void Add(
        GameState state, ZoneId deck, string id, string name, int copies,
        string color, string symbol, int? number = null)
    {
        var properties = new Dictionary<string, PropertyValue>
        {
            ["color"] = color,
            ["symbol"] = symbol,
        };
        if (number is { } n)
            properties["number"] = n;

        state.AddDefinition(new CardDefinition(new CardDefinitionId(id), name, properties: properties));
        for (int i = 0; i < copies; i++)
            state.CreateCard(new CardDefinitionId(id), deck);
    }
}
