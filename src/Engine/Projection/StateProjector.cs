namespace TabletopCore.Engine.Projection;

public static class StateProjector
{
    public static PlayerView ProjectFor(GameState state, PlayerId viewer)
    {
        var players = state.Players.Values
            .OrderBy(p => p.Seat)
            .Select(p => new PlayerSummary(p.Id, p.DisplayName, p.Seat))
            .ToList();

        var zones = state.Zones.Values
            .Select(zone => ProjectZone(state, zone, viewer))
            .ToList();

        var turn = new TurnInfo(state.Turn.ActivePlayer, state.Turn.Direction, state.Turn.TurnNumber);
        return new PlayerView(viewer, players, zones, turn);
    }

    private static ZoneView ProjectZone(GameState state, Zone zone, PlayerId viewer)
    {
        bool contentsVisible = zone.Visibility switch
        {
            ZoneVisibility.Public => true,
            ZoneVisibility.OwnerOnly => zone.Owner == viewer,
            _ => false,
        };

        IReadOnlyList<CardView>? cards = null;
        if (contentsVisible)
        {
            cards = zone.Cards.Select(id =>
            {
                var card = state.GetCard(id);
                // In an OwnerOnly zone the owner sees everything; in a Public
                // zone a face-down card is a visible back with a hidden identity.
                bool identityVisible = zone.Visibility == ZoneVisibility.OwnerOnly || card.FaceUp;
                return new CardView(card.Id, identityVisible ? card.Definition : null, card.FaceUp);
            }).ToList();
        }

        return new ZoneView(zone.Id, zone.Kind, zone.Visibility, zone.Owner, zone.Count, cards, zone.Counters);
    }
}
