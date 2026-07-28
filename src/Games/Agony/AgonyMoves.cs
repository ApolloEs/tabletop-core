using TabletopCore.Engine;

namespace TabletopCore.Games.Agony;

public abstract record AgonyMove : GameMove;

/// <summary>Play a card from your hand. Wilds must declare a color;
/// colored cards must not.</summary>
public sealed record PlayCard(CardInstanceId Card, AgonyColor? DeclaredColor = null) : AgonyMove;

/// <summary>Draw from the deck: one card normally (after which you may play
/// only that card, or pass), or the whole accumulated debt when a +2/+4 is
/// pending — which also ends your turn.</summary>
public sealed record DrawCard : AgonyMove;

/// <summary>End your turn after drawing (or when there is nothing to draw).</summary>
public sealed record PassTurn : AgonyMove;
