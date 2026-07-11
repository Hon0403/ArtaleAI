using System.Drawing;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.UI.MapEditing;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 地圖編輯器 Validation 邏輯層：在 BuildHTopology 之後判斷資料與連通性。
    /// </summary>
    public static class MapEditorValidationService
    {
        private const float HeightTolerance = 2.0f;
        private const float MinSegmentLength = 3.0f;
        private const float MinPointSpacing = 1.0f;
        private const float AnchorResolveMaxDist = 1.5f;

        public static MapEditorValidationResult Validate(MapData mapData)
        {
            var issues = new List<MapEditorValidationIssue>();
            mapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
            mapData.Ropes ??= new List<float[]>();
            mapData.JumpLinks ??= new List<float[]>();
            mapData.ManualEdgeAnchors ??= new List<ManualEdgeAnchor>();
            mapData.Nodes ??= new List<NavNodeData>();
            mapData.Edges ??= new List<NavEdgeData>();

            var platformById = mapData.PolylinePlatforms
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            ValidateSegments(mapData, issues);
            ValidateRopes(mapData, issues);
            ValidateJumpLinks(mapData, issues);
            ValidateManualEdgeAnchors(mapData, platformById, issues);

            int componentCount = AnalyzeConnectivity(mapData, issues);
            DetectOneWayGaps(mapData, issues);
            DetectOrphanPlatforms(mapData, issues);

            var ordered = issues
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.Code, StringComparer.Ordinal)
                .ToList();

            return new MapEditorValidationResult
            {
                Issues = ordered,
                ConnectedComponentCount = componentCount
            };
        }

        private static void ValidateSegments(MapData mapData, List<MapEditorValidationIssue> issues)
        {
            foreach (var plat in mapData.PolylinePlatforms)
            {
                if (plat.Points == null || plat.Points.Count < 2)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Segment",
                        Severity = MapEditorValidationSeverity.Warning,
                        Message = $"平台「{plat.Id}」折點不足 2 個。",
                        TargetKind = MapEditorValidationTargetKind.Platform,
                        TargetPlatform = plat
                    });
                    continue;
                }

                for (int i = 0; i < plat.Points.Count - 1; i++)
                {
                    float dx = plat.Points[i + 1].X - plat.Points[i].X;
                    float dy = plat.Points[i + 1].Y - plat.Points[i].Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < MinSegmentLength)
                    {
                        issues.Add(new MapEditorValidationIssue
                        {
                            Code = "V-Segment",
                            Severity = MapEditorValidationSeverity.Warning,
                            Message = $"平台「{plat.Id}」segment {i} 過短（{len:F1}px）。",
                            TargetKind = MapEditorValidationTargetKind.Platform,
                            TargetPlatform = plat
                        });
                    }
                }

                for (int i = 0; i < plat.Points.Count - 1; i++)
                {
                    float dx = plat.Points[i + 1].X - plat.Points[i].X;
                    float dy = plat.Points[i + 1].Y - plat.Points[i].Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist < MinPointSpacing)
                    {
                        issues.Add(new MapEditorValidationIssue
                        {
                            Code = "V-Segment",
                            Severity = MapEditorValidationSeverity.Warning,
                            Message = $"平台「{plat.Id}」折點 {i}/{i + 1} 過密（{dist:F1}px）。",
                            TargetKind = MapEditorValidationTargetKind.Platform,
                            TargetPlatform = plat
                        });
                    }
                }
            }
        }

        private static void ValidateRopes(MapData mapData, List<MapEditorValidationIssue> issues)
        {
            for (int i = 0; i < mapData.Ropes.Count; i++)
            {
                var rope = mapData.Ropes[i];
                if (rope.Length < 3)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Rope",
                        Severity = MapEditorValidationSeverity.Error,
                        Message = $"繩索 #{i} 資料格式無效。",
                        TargetKind = MapEditorValidationTargetKind.Rope,
                        TargetRopeIndex = i
                    });
                    continue;
                }

                float ropeX = rope[0];
                float topY = rope[1];
                float bottomY = rope[2];
                bool hasTop = mapData.Nodes.Any(n => Distance(n.X, n.Y, ropeX, topY) <= HeightTolerance);
                bool hasBottom = mapData.Nodes.Any(n => Distance(n.X, n.Y, ropeX, bottomY) <= HeightTolerance);
                bool hasClimb = mapData.Edges.Any(e =>
                    e.ActionType is NavigationActionType.ClimbUp or NavigationActionType.ClimbDown &&
                    EdgeTouchesVerticalChannel(e, mapData.Nodes, ropeX, topY, bottomY));

                if (!hasTop || !hasBottom || !hasClimb)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Rope",
                        Severity = MapEditorValidationSeverity.Error,
                        Message = $"繩索 #{i}（X={ropeX:F1}）未能投影到上下平台或未建立 Climb 邊。",
                        TargetKind = MapEditorValidationTargetKind.Rope,
                        TargetRopeIndex = i
                    });
                }
            }
        }

        private static void ValidateJumpLinks(MapData mapData, List<MapEditorValidationIssue> issues)
        {
            for (int i = 0; i < mapData.JumpLinks.Count; i++)
            {
                var link = mapData.JumpLinks[i];
                if (link.Length < 3)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-JumpLink",
                        Severity = MapEditorValidationSeverity.Error,
                        Message = $"跳點 #{i} 資料格式無效。",
                        TargetKind = MapEditorValidationTargetKind.JumpLink,
                        TargetJumpLinkIndex = i
                    });
                    continue;
                }

                float linkX = link[0];
                float topY = link[1];
                float bottomY = link[2];
                bool hasTop = mapData.Nodes.Any(n => Distance(n.X, n.Y, linkX, topY) <= HeightTolerance);
                bool hasBottom = mapData.Nodes.Any(n => Distance(n.X, n.Y, linkX, bottomY) <= HeightTolerance);
                bool hasJump = mapData.Edges.Any(e =>
                    e.ActionType is NavigationActionType.Jump or NavigationActionType.JumpDown &&
                    EdgeTouchesVerticalChannel(e, mapData.Nodes, linkX, topY, bottomY));

                if (!hasTop || !hasBottom || !hasJump)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-JumpLink",
                        Severity = MapEditorValidationSeverity.Error,
                        Message = $"跳點 #{i}（X={linkX:F1}）未能投影到上下平台或未建立 Jump 邊。",
                        TargetKind = MapEditorValidationTargetKind.JumpLink,
                        TargetJumpLinkIndex = i
                    });
                }
            }
        }

        private static void ValidateManualEdgeAnchors(
            MapData mapData,
            Dictionary<string, PolylinePlatformData> platformById,
            List<MapEditorValidationIssue> issues)
        {
            var nodeById = mapData.Nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .ToDictionary(n => n.Id, StringComparer.Ordinal);

            var edgeKeys = mapData.Edges
                .Select(e => (e.FromNodeId, e.ToNodeId))
                .ToHashSet();

            foreach (var anchor in mapData.ManualEdgeAnchors)
            {
                if (!platformById.ContainsKey(anchor.FromPlatformId))
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Anchor-Id",
                        Severity = MapEditorValidationSeverity.Error,
                        Message = $"手動邊起點平台「{anchor.FromPlatformId}」不存在。",
                        TargetKind = MapEditorValidationTargetKind.ManualEdge,
                        TargetManualEdge = anchor
                    });
                }

                if (!platformById.ContainsKey(anchor.ToPlatformId))
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Anchor-Id",
                        Severity = MapEditorValidationSeverity.Error,
                        Message = $"手動邊終點平台「{anchor.ToPlatformId}」不存在。",
                        TargetKind = MapEditorValidationTargetKind.ManualEdge,
                        TargetManualEdge = anchor
                    });
                }

                bool fromResolved = TryResolveNodeId(nodeById, anchor.FromPlatformId, anchor.FromX, anchor.FromY, out string fromId);
                bool toResolved = TryResolveNodeId(nodeById, anchor.ToPlatformId, anchor.ToX, anchor.ToY, out string toId);

                if (!fromResolved || !toResolved)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Anchor-Resolve",
                        Severity = MapEditorValidationSeverity.Error,
                        Message =
                            $"手動邊 {anchor.FromPlatformId}→{anchor.ToPlatformId} 錨點解析失敗（{anchor.ActionType}）。",
                        TargetKind = MapEditorValidationTargetKind.ManualEdge,
                        TargetManualEdge = anchor
                    });
                    continue;
                }

                if (!edgeKeys.Contains((fromId, toId)))
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Anchor-Resolve",
                        Severity = MapEditorValidationSeverity.Error,
                        Message =
                            $"手動邊 {anchor.FromPlatformId}→{anchor.ToPlatformId} 未生成 runtime 邊（{anchor.ActionType}）。",
                        TargetKind = MapEditorValidationTargetKind.ManualEdge,
                        TargetManualEdge = anchor
                    });
                }
            }
        }

        private static int AnalyzeConnectivity(MapData mapData, List<MapEditorValidationIssue> issues)
        {
            if (mapData.Nodes.Count == 0)
                return 0;

            var adjacency = BuildUndirectedAdjacency(mapData.Edges);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var components = new List<List<string>>();

            foreach (var node in mapData.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id) || visited.Contains(node.Id))
                    continue;

                var component = new List<string>();
                var queue = new Queue<string>();
                queue.Enqueue(node.Id);
                visited.Add(node.Id);

                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    component.Add(current);
                    if (!adjacency.TryGetValue(current, out var neighbors))
                        continue;

                    foreach (string next in neighbors)
                    {
                        if (visited.Add(next))
                            queue.Enqueue(next);
                    }
                }

                components.Add(component);
            }

            if (components.Count > 1)
            {
                var summaries = components
                    .Select((nodes, index) => $"子圖{index + 1}: {DescribeComponentPlatforms(mapData, nodes)}")
                    .ToList();

                issues.Add(new MapEditorValidationIssue
                {
                    Code = "V-Connectivity",
                    Severity = MapEditorValidationSeverity.Warning,
                    Message = $"地圖有 {components.Count} 個不相連子圖（{string.Join(" | ", summaries)}）。",
                    TargetKind = MapEditorValidationTargetKind.None
                });
            }

            return Math.Max(components.Count, 1);
        }

        private static void DetectOneWayGaps(MapData mapData, List<MapEditorValidationIssue> issues)
        {
            if (mapData.ManualEdgeAnchors.Count == 0)
                return;

            var nodeById = mapData.Nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .ToDictionary(n => n.Id, StringComparer.Ordinal);
            var directed = BuildDirectedAdjacency(mapData.Edges);

            foreach (var anchor in mapData.ManualEdgeAnchors)
            {
                if (anchor.ActionType is NavigationActionType.Walk or NavigationActionType.ClimbUp
                    or NavigationActionType.ClimbDown)
                    continue;

                if (!TryResolveNodeId(nodeById, anchor.FromPlatformId, anchor.FromX, anchor.FromY, out string fromId))
                    continue;
                if (!TryResolveNodeId(nodeById, anchor.ToPlatformId, anchor.ToX, anchor.ToY, out string toId))
                    continue;
                if (string.Equals(fromId, toId, StringComparison.Ordinal))
                    continue;

                if (!HasDirectedPath(directed, toId, fromId))
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-OneWay",
                        Severity = MapEditorValidationSeverity.Info,
                        Message =
                            $"手動邊 {anchor.FromPlatformId}→{anchor.ToPlatformId}（{anchor.ActionType}）無回程路徑，可能無法返回。",
                        TargetKind = MapEditorValidationTargetKind.ManualEdge,
                        TargetManualEdge = anchor
                    });
                }
            }
        }

        private static void DetectOrphanPlatforms(MapData mapData, List<MapEditorValidationIssue> issues)
        {
            var connectedNodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in mapData.Edges)
            {
                if (!string.IsNullOrWhiteSpace(edge.FromNodeId))
                    connectedNodes.Add(edge.FromNodeId);
                if (!string.IsNullOrWhiteSpace(edge.ToNodeId))
                    connectedNodes.Add(edge.ToNodeId);
            }

            foreach (var plat in mapData.PolylinePlatforms)
            {
                bool hasEdge = mapData.Nodes.Any(n =>
                    string.Equals(n.PlatformId, plat.Id, StringComparison.Ordinal) &&
                    connectedNodes.Contains(n.Id));

                if (!hasEdge)
                {
                    issues.Add(new MapEditorValidationIssue
                    {
                        Code = "V-Orphan",
                        Severity = MapEditorValidationSeverity.Warning,
                        Message = $"平台「{plat.Id}」孤立（無任何邊連接）。",
                        TargetKind = MapEditorValidationTargetKind.Platform,
                        TargetPlatform = plat
                    });
                }
            }
        }

        private static bool TryResolveNodeId(
            Dictionary<string, NavNodeData> nodeById,
            string platformId,
            float anchorX,
            float anchorY,
            out string nodeId)
        {
            nodeId = MapGenerationService.BuildVirtualNodeId(platformId, anchorX, anchorY);
            if (nodeById.ContainsKey(nodeId))
                return true;

            string prefix = $"n_v_plat_{platformId}_";
            NavNodeData? best = null;
            float bestDist = AnchorResolveMaxDist;

            foreach (var node in nodeById.Values)
            {
                if (!node.Id.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                float dist = Distance(node.X, node.Y, anchorX, anchorY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = node;
                }
            }

            if (best == null)
                return false;

            nodeId = best.Id;
            return true;
        }

        private static Dictionary<string, HashSet<string>> BuildUndirectedAdjacency(IEnumerable<NavEdgeData> edges)
        {
            var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void Link(string a, string b)
            {
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                    return;
                if (!adjacency.TryGetValue(a, out var setA))
                {
                    setA = new HashSet<string>(StringComparer.Ordinal);
                    adjacency[a] = setA;
                }
                setA.Add(b);
                if (!adjacency.TryGetValue(b, out var setB))
                {
                    setB = new HashSet<string>(StringComparer.Ordinal);
                    adjacency[b] = setB;
                }
                setB.Add(a);
            }

            foreach (var edge in edges)
                Link(edge.FromNodeId, edge.ToNodeId);

            return adjacency;
        }

        private static Dictionary<string, List<string>> BuildDirectedAdjacency(IEnumerable<NavEdgeData> edges)
        {
            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                if (string.IsNullOrWhiteSpace(edge.FromNodeId) || string.IsNullOrWhiteSpace(edge.ToNodeId))
                    continue;
                if (!adjacency.TryGetValue(edge.FromNodeId, out var list))
                {
                    list = new List<string>();
                    adjacency[edge.FromNodeId] = list;
                }
                list.Add(edge.ToNodeId);
            }

            return adjacency;
        }

        private static bool HasDirectedPath(Dictionary<string, List<string>> directed, string start, string goal)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (string.Equals(current, goal, StringComparison.Ordinal))
                    return true;

                if (!directed.TryGetValue(current, out var neighbors))
                    continue;

                foreach (string next in neighbors)
                {
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            return false;
        }

        private static string DescribeComponentPlatforms(MapData mapData, List<string> nodeIds)
        {
            var ids = nodeIds
                .Select(id => mapData.Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal)))
                .Where(n => n != null && !string.IsNullOrWhiteSpace(n.PlatformId))
                .Select(n => n!.PlatformId!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            return ids.Count == 0 ? "（無平台）" : string.Join(", ", ids);
        }

        private static bool EdgeTouchesVerticalChannel(
            NavEdgeData edge,
            List<NavNodeData> nodes,
            float ropeX,
            float topY,
            float bottomY)
        {
            var from = nodes.FirstOrDefault(n => string.Equals(n.Id, edge.FromNodeId, StringComparison.Ordinal));
            var to = nodes.FirstOrDefault(n => string.Equals(n.Id, edge.ToNodeId, StringComparison.Ordinal));
            if (from == null || to == null)
                return false;

            bool fromOnRope = Math.Abs(from.X - ropeX) <= HeightTolerance &&
                (Math.Abs(from.Y - topY) <= HeightTolerance || Math.Abs(from.Y - bottomY) <= HeightTolerance);
            bool toOnRope = Math.Abs(to.X - ropeX) <= HeightTolerance &&
                (Math.Abs(to.Y - topY) <= HeightTolerance || Math.Abs(to.Y - bottomY) <= HeightTolerance);

            return fromOnRope && toOnRope;
        }

        private static float Distance(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
