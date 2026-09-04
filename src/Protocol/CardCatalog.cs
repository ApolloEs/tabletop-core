using TabletopCore.Engine;

namespace TabletopCore.Protocol;

/// <summary>
/// The definition catalog a client receives once per game (after start, and
/// again on resume). PlayerView deliberately carries only CardDefinitionIds;
/// this is where an id becomes renderable — name, property bag, face image.
/// It is also, by design, the exact shape a future cards.json content deck
/// will feed: when custom decks arrive, they fill this same DTO and no
/// client changes.
/// </summary>
public sealed record CardCatalog(IReadOnlyList<CardCatalogEntry> Definitions)
{
    /// <summary>Ordered by definition id so the payload is deterministic —
    /// golden tests and caching both like stable bytes.</summary>
    public static CardCatalog From(GameState state) => new(
        state.Definitions.Values
            .OrderBy(d => d.Id.Value, StringComparer.Ordinal)
            .Select(d => new CardCatalogEntry(d.Id, d.Name, d.Properties, d.FaceImage))
            .ToList());
}

/// <summary>FaceImage is null for programmatic decks (Agony renders from
/// properties); custom decks put a URL here and the client renders that
/// instead. It stays URL-shaped forever — the server serves content, the
/// client never receives image bytes over the game connection.</summary>
public sealed record CardCatalogEntry(
    CardDefinitionId Id,
    string Name,
    IReadOnlyDictionary<string, PropertyValue> Properties,
    string? FaceImage);
