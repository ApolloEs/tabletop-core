using System.Text.Json;
using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using Xunit;

namespace TabletopCore.Protocol.Tests;

/// <summary>
/// The adversarial sweep: play whole games and assert the serialized bytes a
/// client would receive never name a card the viewer cannot see. The oracle
/// re-derives visibility from raw state by hand — deliberately NOT by calling
/// StateProjector, which is the code under suspicion.
/// (Instance ids are allowed to appear anywhere: their binding to definitions
/// is seed-randomized at setup, so they carry no identity — see AgonyDeck.)
/// </summary>
public class LeakTests
{
    public static TheoryData<int, int> SeedsAndPlayerCounts() => new()
    {
        { 1, 2 }, { 2, 3 }, { 3, 4 }, { 4, 3 }, { 5, 2 },
    };

    [Theory]
    [MemberData(nameof(SeedsAndPlayerCounts))]
    public void PlayingThroughAGameNeverLeaksAHiddenCard(int seed, int playerCount)
    {
        var state = new GameState(seed: (ulong)seed);
        for (int i = 0; i < playerCount; i++)
            state.AddPlayer($"Player{i}");
        var game = new AgonyGame(new AgonyConfig { StackDrawTwo = true, SwapRotateCards = true });
        game.Setup(state);

        for (int step = 0; step < 80 && game.GetWinner(state) is null; step++)
        {
            var actor = state.Turn.ActivePlayer;
            var moves = game.GetLegalMoves(state, actor);
            Assert.NotEmpty(moves);

            var result = game.TryMove(state, actor, moves[step * 7 % moves.Count]);
            Assert.True(result.Success, result.Error);

            foreach (var viewer in state.Players.Keys)
            {
                AssertViewLeaksNothing(state, viewer);
                AssertEventsLeakNothing(result.Events, viewer);
            }
        }
    }

    private static void AssertViewLeaksNothing(GameState state, PlayerId viewer)
    {
        // Hand-derived oracle mirroring the visibility rules: a viewer may
        // see a card's definition iff it is face-up in a Public zone, or in
        // an OwnerOnly zone they own.
        var allowed = new HashSet<CardDefinitionId>();
        foreach (var card in state.Cards.Values)
        {
            var zone = state.Zones[card.Zone];
            bool visible = zone.Visibility switch
            {
                ZoneVisibility.Public => card.FaceUp,
                ZoneVisibility.OwnerOnly => zone.Owner == viewer,
                _ => false,
            };
            if (visible)
                allowed.Add(card.Definition);
        }

        string json = JsonSerializer.Serialize(StateProjector.ProjectFor(state, viewer), WireJson.Options);
        foreach (var definitionId in state.Definitions.Keys)
        {
            if (!allowed.Contains(definitionId))
                Assert.DoesNotContain($"\"{definitionId.Value}\"", json, StringComparison.Ordinal);
        }
    }

    private static void AssertEventsLeakNothing(IReadOnlyList<GameEvent> rawEvents, PlayerId viewer)
    {
        foreach (var gameEvent in EventProjector.ProjectFor(rawEvents, viewer))
        {
            var drawn = gameEvent as CardDrawn;
            if (drawn is not null)
                Assert.Equal(viewer, drawn.Player);
        }
    }
}
