using TabletopCore.Engine;

namespace TabletopCore.Engine.Tests;

/// <summary>
/// A minimal two-player fixture: 20 definitions (red/blue 0–9), a hidden
/// deck holding all 20 cards, a public discard, and one OwnerOnly hand per
/// player. Not UNO — just enough structure to exercise the engine.
/// </summary>
internal static class TestGame
{
    public static readonly ZoneId Deck = new("deck");
    public static readonly ZoneId Discard = new("discard");

    public static ZoneId HandOf(Player player) => new($"hand:{player.Seat}");

    public static GameState Create(ulong seed, out Player alice, out Player bob)
    {
        var state = new GameState(seed);

        foreach (var color in new[] { "red", "blue" })
        {
            for (int n = 0; n <= 9; n++)
            {
                state.AddDefinition(new CardDefinition(
                    new CardDefinitionId($"{color}-{n}"),
                    $"{color} {n}",
                    properties: new Dictionary<string, PropertyValue>
                    {
                        ["color"] = color,
                        ["number"] = n,
                    }));
            }
        }

        alice = state.AddPlayer("Alice");
        bob = state.AddPlayer("Bob");

        state.AddZone(Deck, ZoneKind.Pile, ZoneVisibility.Hidden);
        state.AddZone(Discard, ZoneKind.Pile, ZoneVisibility.Public);
        state.AddZone(HandOf(alice), ZoneKind.Hand, ZoneVisibility.OwnerOnly, alice.Id);
        state.AddZone(HandOf(bob), ZoneKind.Hand, ZoneVisibility.OwnerOnly, bob.Id);

        foreach (var definition in state.Definitions.Keys.ToList())
            state.CreateCard(definition, Deck);

        return state;
    }
}
