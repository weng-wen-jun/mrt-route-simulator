using MrtRouteSimulator.Engine;

/// <summary>
/// Local handbook geometry, with explicit train-end clearances. The manual's
/// s_p1/s_p2 are gaps next to the train: formula (2.42) adds L separately.
/// Platform centres therefore move by L/2; tail stop positions are head positions.
/// The 25 m beyond the tail head and platform length=L are synthetic allowances.
/// </summary>
public static class TurnbackGeometryBuilder
{
    public static TopologyProjectDocument Align(TopologyProjectDocument source,
        StationLayoutTemplateKind kind, ProjectSpatialReferencePoint point, double trainLength)
    {
        var topology = source.Topology;
        var front = kind == StationLayoutTemplateKind.FrontTurnback;
        var gap = point.DistanceFromStopToCrossoverMeters;
        var half = trainLength / 2;
        var edges = topology.Edges.Select(edge => front && (edge.TrackEdgeId is "DC" or "UC")
            ? edge with { LengthMeters = point.CrossoverLengthMeters } : edge).ToArray();
        var platforms = topology.Platforms.Select(platform =>
        {
            if (platform.StationId != "A") return platform;
            // D0 is oriented away from the west terminal; U0 is oriented toward it.
            // Front: outgoing head / arriving rear has gap s_p1.
            // Rear: incoming head / returning rear has gap s_p1.
            var length = edges.Single(edge => edge.TrackEdgeId == platform.TrackEdgeId).LengthMeters;
            var centre = (platform.TrackEdgeId == "D0") == front
                ? length - gap - half : gap + half;
            return platform with { StopPositionReference = StopPositionReference.TrainCenter,
                StopPositionOffsetMeters = centre, PlatformStartOffsetMeters = centre - half,
                PlatformEndOffsetMeters = centre + half, EffectiveLengthMeters = trainLength };
        }).ToArray();
        double Centre(string id) => platforms.Single(platform => platform.PlatformId == id).StopPositionOffsetMeters;
        var facilities = topology.TurnbackFacilities.Select(facility => facility with
        {
            ArrivalStopOffsetMeters = front
                ? Centre(facility.FacilityId == "TURN" ? "A:D" : "A:U") : Centre("A:U"),
            DepartureStartOffsetMeters = front
                ? Centre(facility.FacilityId == "TURN" ? "A:D" : "A:U") : Centre("A:D")
        }).ToArray();
        if (front)
        {
            facilities = facilities.Select(facility => facility with
            {
                TurnbackStopPosition = new TrackPosition(facility.ArrivalTrackEdgeId, facility.ArrivalStopOffsetMeters)
            }).ToArray();
        }
        else
        {
            // Full crossover zone: each straight and diagonal path is s_c long.
            // Port A faces the tails, B faces the station on all four boundary nodes.
            // This replaces the staggered template's 200 m approaches with the
            // handbook's 160 m crossover zone; no hidden 100 m leads remain.
            edges = edges.Where(edge => edge.TrackEdgeId is not ("TU1" or "TD1")).Select(edge => edge.TrackEdgeId switch
            {
                "TU0" => edge with { ToNodeId = "U200", LengthMeters = point.CrossoverLengthMeters,
                    FromPortSide = TrackPortSide.A, ToPortSide = TrackPortSide.B },
                "TD0" => edge with { FromNodeId = "D200", LengthMeters = point.CrossoverLengthMeters,
                    FromPortSide = TrackPortSide.B, ToPortSide = TrackPortSide.A },
                "X1" => edge with { ToNodeId = "DN0", FromPortSide = TrackPortSide.B, ToPortSide = TrackPortSide.A },
                "X2" => edge with { FromNodeId = "UN0", FromPortSide = TrackPortSide.A, ToPortSide = TrackPortSide.B },
                "TU2" or "TD2" => edge with { LengthMeters = trainLength + point.DistanceFromCrossoverToTurnbackStopMeters + 25 },
                _ => edge
            }).ToArray();
            var penetration = trainLength + point.DistanceFromCrossoverToTurnbackStopMeters;
            facilities = facilities.Select(facility => facility.FacilityId == "TURN"
                ? facility with { Traversals = [T("TU0"), T("TU2"), T("TU2", true), T("X1")],
                    TurnbackStopPosition = new TrackPosition("TU2", penetration) }
                : facility with { Traversals = [T("X2"), T("TD2", true), T("TD2"), T("TD0")],
                    TurnbackStopPosition = new TrackPosition("TD2", 25) }).ToArray();
        }
        topology = topology with { Nodes = front ? topology.Nodes : topology.Nodes.Where(node => node.NodeId is not ("U100" or "D100")).ToArray(),
            Edges = edges, Platforms = platforms, TurnbackFacilities = facilities,
            DirectedConnections = [] };
        topology = topology with { DirectedConnections = DirectedTrackConnectionRules.Build(topology, source.ServiceRoutes) };
        var document = source with { Topology = topology };
        TopologyProjectFormat.Validate(document);
        return document;
    }

    public static TopologyProjectDocument ApplyGrades(TopologyProjectDocument source,
        StationLayoutTemplateKind kind, double grade)
    {
        var front = kind == StationLayoutTemplateKind.FrontTurnback;
        // Manual gives terminal approach grade (front) or tail-to-station exit
        // grade (rear), not a surveyed profile. Extend that local incline over
        // these explicit full edges as a modelling assumption. Opposite traversal
        // reverses the sign in the Engine; no independent directional grade exists.
        var local = front ? new HashSet<string>(["D0", "U0", "D1", "U1", "DC", "UC"])
            : new HashSet<string>(["D0", "U0", "TU0", "TD0", "TU2", "TD2"]);
        var segments = source.Topology.Edges.Where(edge => local.Contains(edge.TrackEdgeId)
                || edge.TrackEdgeId.StartsWith("X1:") || edge.TrackEdgeId.StartsWith("X2:"))
            .Select(edge => new TrackGradientSegment($"GRADE:{edge.TrackEdgeId}", edge.TrackEdgeId, 0, edge.LengthMeters,
                grade * (front
                    ? (edge.TrackEdgeId.StartsWith("U") || edge.TrackEdgeId.StartsWith("X") ? 1 : -1)
                    : (edge.TrackEdgeId.StartsWith("D") || edge.TrackEdgeId.StartsWith("TD")
                        || edge.TrackEdgeId.StartsWith("X1:") ? 1 : -1))))
            .ToArray();
        var document = source with { Topology = source.Topology with { Gradients = segments } };
        TopologyProjectFormat.Validate(document);
        return document;
    }

    private static DirectedTrackTraversal T(string id, bool reverse = false) =>
        new(id, reverse ? TraversalDirection.Reverse : TraversalDirection.Forward);
}
