namespace TabletopCore.Engine;

/// <summary>
/// A card at runtime. Mutable, but only the engine mutates it — all changes
/// flow through the action pipeline (internal setters).
/// </summary>
public sealed class CardInstance
{
    internal CardInstance(CardInstanceId id, CardDefinitionId definition, ZoneId zone, bool faceUp, PlayerId? owner)
    {
        Id = id;
        Definition = definition;
        Zone = zone;
        FaceUp = faceUp;
        Owner = owner;
    }

    public CardInstanceId Id { get; }
    public CardDefinitionId Definition { get; }
    public ZoneId Zone { get; internal set; }
    public bool FaceUp { get; internal set; }
    public PlayerId? Owner { get; internal set; }
}
