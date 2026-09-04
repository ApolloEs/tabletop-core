using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Games.Agony;

namespace TabletopCore.Protocol.Json;

/// <summary>
/// Registers the polymorphic wire hierarchies. GameEvent and GameMove are
/// abstract on purpose — a State payload carries "a list of events" — so the
/// JSON needs a discriminator ("type") naming each concrete record, and the
/// deserializer needs a closed registry mapping names back to types (a closed
/// list is also the security posture: a client can only ever instantiate
/// types registered here).
///
/// Registered centrally instead of [JsonDerivedType] attributes on the base
/// records because the bases live in Engine, which cannot name Agony's
/// derived types — and Engine stays JSON-free regardless (its charter).
///
/// Discriminator strings are wire contract, frozen by the golden JSON tests:
/// renaming a C# record must not silently rename the wire — change the
/// mapping here consciously or not at all. Engine events are bare camelCase;
/// moves are game-prefixed ("agony.playCard") since move vocabularies are
/// per-game and game #2's moves will share this registry.
/// </summary>
internal static class WireTypeResolver
{
    private static readonly (Type Type, string Name)[] Events =
    [
        (typeof(CardMoved), "cardMoved"),
        (typeof(CardFlipped), "cardFlipped"),
        (typeof(ZoneShuffled), "zoneShuffled"),
        (typeof(CardDrawn), "cardDrawn"),
        (typeof(CardsDrawnHidden), "cardsDrawnHidden"),
        (typeof(TurnEnded), "turnEnded"),
        (typeof(TurnStarted), "turnStarted"),
        (typeof(TurnSkipped), "turnSkipped"),
        (typeof(TurnDirectionChanged), "turnDirectionChanged"),
        (typeof(CounterChanged), "counterChanged"),
    ];

    private static readonly (Type Type, string Name)[] Moves =
    [
        (typeof(PlayCard), "agony.playCard"),
        (typeof(DrawCard), "agony.drawCard"),
        (typeof(PassTurn), "agony.passTurn"),
    ];

    public static IJsonTypeInfoResolver Create()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type == typeof(GameEvent))
                typeInfo.PolymorphismOptions = BuildOptions(Events);
            else if (typeInfo.Type == typeof(GameMove))
                typeInfo.PolymorphismOptions = BuildOptions(Moves);
        });
        return resolver;
    }

    private static JsonPolymorphismOptions BuildOptions((Type Type, string Name)[] derived)
    {
        var options = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "type",
            // Unknown concrete type reaching serialization = server bug: fail loudly.
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var (type, name) in derived)
            options.DerivedTypes.Add(new JsonDerivedType(type, name));
        return options;
    }
}
