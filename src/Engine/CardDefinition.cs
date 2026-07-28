using System.Collections.ObjectModel;

namespace TabletopCore.Engine;

/// <summary>
/// Immutable card data, loaded from content packages (cards.json) — never
/// hardcoded by rules. The face image is a path/URL the server serves and
/// the client renders. Rules must not read <see cref="Properties"/> directly;
/// they go through <see cref="GameState.GetValue"/> so a modifier layer can
/// intercept later (L14).
/// </summary>
public sealed record CardDefinition
{
    public CardDefinition(
        CardDefinitionId id,
        string name,
        string? faceImage = null,
        IReadOnlyDictionary<string, PropertyValue>? properties = null,
        IReadOnlyList<EffectEntry>? effects = null)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FaceImage = faceImage;
        Properties = properties ?? ReadOnlyDictionary<string, PropertyValue>.Empty;
        Effects = effects ?? [];
    }

    public CardDefinitionId Id { get; }
    public string Name { get; }
    public string? FaceImage { get; }
    public IReadOnlyDictionary<string, PropertyValue> Properties { get; }
    public IReadOnlyList<EffectEntry> Effects { get; }
}

/// <summary>
/// A reference to a pre-built effect primitive plus its parameters — the
/// data-driven hook the card editor composes. No code, no scripting (L7).
/// </summary>
public sealed record EffectEntry(string EffectId, IReadOnlyDictionary<string, PropertyValue> Parameters);
