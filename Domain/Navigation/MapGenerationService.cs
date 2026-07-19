using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ArtaleAI.Models.Map;
using ArtaleAI.Shared;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>
    /// 地圖導航拓撲生成：將幾何平台與繩索資源轉為導航網格（自動切段、建立相鄰邊與爬繩邊）。
    /// </summary>
    public static class MapGenerationService
    {
        private const string VirtualNodePrefix = "n_v_";
        private const float HeightTolerance = 2.0f; // 判斷繩索點是否落在平台上的高度容許值

        /// <summary>
        /// 垂直通道成本：以高度為底，再乘懲罰倍率，讓 A* 在有平台繞路時優先 Walk。
        /// 唯一通道時仍可達（成本再高也會走）。
        /// </summary>
        private const float ClimbUpCostPerPx = 2.5f;
        private const float ClimbDownCostPerPx = 1.5f;
        private const float JumpUpCostPerPx = 3.0f;
        private const float JumpDownCostPerPx = 1.2f;
        private const float ClimbUpMinCost = 12.0f;
        private const float ClimbDownMinCost = 8.0f;
        private const float JumpUpMinCost = 16.0f;
        private const float JumpDownMinCost = 6.0f;

        /// <summary>
        /// 安全區仍是可走通道（維持連通），但 Walk 成本加權，
        /// 有一般平台替代時 A* 較少在安全區上徘徊。
        /// </summary>
        private const float SafeZoneWalkCostMultiplier = 2.5f;

        /// <summary>
        /// 自動根據 PolylinePlatforms 與 Ropes 重建地圖導航拓撲。
        /// 本實作具備冪等性 (Idempotency)，多次重複呼叫產出的拓撲圖結構與順序將完全一致。
        /// </summary>
        public static void BuildHTopology(MapData mapData)
        {
            if (mapData == null) return;

            mapData.Nodes ??= new List<NavNodeData>();
            mapData.Edges ??= new List<NavEdgeData>();
            mapData.Ropes ??= new List<float[]>();
            mapData.JumpLinks ??= new List<float[]>();
            mapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
            mapData.ManualEdgeAnchors ??= new List<ManualEdgeAnchor>();

            // 1. 統一清理所有以 n_v_ 開頭的自動生成虛擬節點與相關有向邊
            mapData.Nodes.RemoveAll(n => n.Id.StartsWith(VirtualNodePrefix, StringComparison.Ordinal));
            mapData.Edges.RemoveAll(e => e.FromNodeId.StartsWith(VirtualNodePrefix, StringComparison.Ordinal) || 
                                         e.ToNodeId.StartsWith(VirtualNodePrefix, StringComparison.Ordinal));

            if (mapData.PolylinePlatforms.Count == 0) return;

            // 2. 執行平台幾何自動切分與拓撲生成（A/B/C 段）
            BuildNewPlatformTopology(mapData);

            // D 段：ManualEdgeAnchors → 例外邊
            ResolveManualEdgeAnchors(mapData);
        }

        /// <summary>由平台 ID 與量化座標組裝虛擬節點 ID，供 UI 與 Resolve 共用。</summary>
        public static string BuildVirtualNodeId(string platformId, float x, float y)
        {
            float qX = (float)Math.Round(x, 1, MidpointRounding.AwayFromZero);
            float qY = (float)Math.Round(y, 1, MidpointRounding.AwayFromZero);
            return $"{VirtualNodePrefix}plat_{platformId}_{Quantize(qX)}_{Quantize(qY)}";
        }

        private static void ResolveManualEdgeAnchors(MapData mapData)
        {
            if (mapData.ManualEdgeAnchors == null || mapData.ManualEdgeAnchors.Count == 0)
                return;

            var nodeById = mapData.Nodes
                .Where(n => !string.IsNullOrEmpty(n.Id))
                .ToDictionary(n => n.Id, StringComparer.Ordinal);

            foreach (var anchor in mapData.ManualEdgeAnchors)
            {
                if (!TryResolvePlatformNodeId(nodeById, anchor.FromPlatformId, anchor.FromX, anchor.FromY, out string fromId))
                {
                    Logger.Warning($"[拓撲] ResolveAnchor 失敗：找不到 fromNode（平台={anchor.FromPlatformId}, X={anchor.FromX:F1}, Y={anchor.FromY:F1}）");
                    continue;
                }

                if (!TryResolvePlatformNodeId(nodeById, anchor.ToPlatformId, anchor.ToX, anchor.ToY, out string toId))
                {
                    Logger.Warning($"[拓撲] ResolveAnchor 失敗：找不到 toNode（平台={anchor.ToPlatformId}, X={anchor.ToX:F1}, Y={anchor.ToY:F1}）");
                    continue;
                }

                if (string.Equals(fromId, toId, StringComparison.Ordinal))
                    continue;

                TryAddDirectedManualEdge(mapData, nodeById, fromId, toId, anchor.ActionType);

                // SideJump 是兩平台間可站立落點的水平跳：能過去原則上能回來，
                // 比照繩索／垂直跳點在拓撲層自動補反向，避免 A* 把回程當不可達。
                if (anchor.ActionType == NavigationActionType.SideJump)
                    TryAddDirectedManualEdge(mapData, nodeById, toId, fromId, NavigationActionType.SideJump);
            }
        }

        private static void TryAddDirectedManualEdge(
            MapData mapData,
            Dictionary<string, NavNodeData> nodeById,
            string fromId,
            string toId,
            NavigationActionType actionType)
        {
            bool duplicate = mapData.Edges.Any(e =>
                string.Equals(e.FromNodeId, fromId, StringComparison.Ordinal) &&
                string.Equals(e.ToNodeId, toId, StringComparison.Ordinal));
            if (duplicate) return;

            float cost = ComputeActionCost(actionType, nodeById[fromId], nodeById[toId]);
            mapData.Edges.Add(new NavEdgeData
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                ActionType = actionType,
                Cost = cost
            });
        }

        /// <summary>
        /// 錨點座標 → runtime node ID。先精確匹配，再在同平台節點中找最近者
        /// （cut point 合併容差會使 UI 點擊座標與實際節點差 0.1~1.0px）。
        /// </summary>
        private static bool TryResolvePlatformNodeId(
            Dictionary<string, NavNodeData> nodeById,
            string platformId,
            float anchorX,
            float anchorY,
            out string nodeId)
        {
            nodeId = BuildVirtualNodeId(platformId, anchorX, anchorY);
            if (nodeById.ContainsKey(nodeId))
                return true;

            string prefix = $"{VirtualNodePrefix}plat_{platformId}_";
            const float maxDist = 1.5f;

            NavNodeData? best = null;
            float bestDist = maxDist;

            foreach (var node in nodeById.Values)
            {
                if (!node.Id.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                float dx = node.X - anchorX;
                float dy = node.Y - anchorY;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
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

        /// <summary>Cost 由 ActionType + 幾何距離決定，不由 UI 預算。</summary>
        private static float ComputeActionCost(NavigationActionType actionType, NavNodeData from, NavNodeData to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            return actionType switch
            {
                NavigationActionType.Walk => dist,
                NavigationActionType.Jump => Math.Max(JumpUpMinCost, dist * JumpUpCostPerPx),
                NavigationActionType.SideJump => Math.Max(JumpUpMinCost, dist * JumpUpCostPerPx),
                NavigationActionType.JumpDown => Math.Max(JumpDownMinCost, dist * JumpDownCostPerPx),
                NavigationActionType.Teleport => 1.0f,
                NavigationActionType.ClimbUp => Math.Max(ClimbUpMinCost, dist * ClimbUpCostPerPx),
                NavigationActionType.ClimbDown => Math.Max(ClimbDownMinCost, dist * ClimbDownCostPerPx),
                _ => dist
            };
        }

        private static void BuildNewPlatformTopology(MapData mapData)
        {
            var generatedNodes = new List<NavNodeData>();
            var generatedEdges = new List<NavEdgeData>();

            var polyPlatforms = mapData.PolylinePlatforms ?? new List<PolylinePlatformData>();

            foreach (var plat in polyPlatforms)
            {
                if (plat.Points == null || plat.Points.Count < 2) continue;

                int N = plat.Points.Count;
                
                // 1. 計算每個頂點的累積弧長 (Arc Length)
                var segmentArcLengths = new float[N];
                segmentArcLengths[0] = 0f;
                for (int i = 0; i < N - 1; i++)
                {
                    float dx = plat.Points[i + 1].X - plat.Points[i].X;
                    float dy = plat.Points[i + 1].Y - plat.Points[i].Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    segmentArcLengths[i + 1] = segmentArcLengths[i] + dist;
                }

                // 2. 初始化 cut points（弧長、座標、安全區旗標）
                var cutPoints = new List<(float ArcLength, PointF Position, bool IsSafeZone)>();
                for (int i = 0; i < N; i++)
                {
                    cutPoints.Add((
                        segmentArcLengths[i],
                        new PointF(plat.Points[i].X, plat.Points[i].Y),
                        plat.Points[i].IsSafeZone));
                }

                // 3. 投影垂直通道端點（繩索與跳點切分）
                foreach (var channel in EnumerateVerticalChannelEndpoints(mapData))
                {
                    var channelPoints = new PointF[]
                    {
                        new PointF(channel.X, channel.TopY),
                        new PointF(channel.X, channel.BottomY)
                    };

                    foreach (var channelP in channelPoints)
                    {
                        float bestDist = float.MaxValue;
                        float bestArcLength = 0f;
                        PointF bestProj = PointF.Empty;
                        int bestSegIdx = -1;

                        for (int i = 0; i < N - 1; i++)
                        {
                            var A = new PointF(plat.Points[i].X, plat.Points[i].Y);
                            var B = new PointF(plat.Points[i + 1].X, plat.Points[i + 1].Y);

                            float abx = B.X - A.X;
                            float aby = B.Y - A.Y;
                            float ab2 = abx * abx + aby * aby;
                            if (ab2 < 0.001f) continue;

                            float apx = channelP.X - A.X;
                            float apy = channelP.Y - A.Y;
                            float t = (apx * abx + apy * aby) / ab2;
                            t = Math.Max(0.0f, Math.Min(1.0f, t));

                            var proj = new PointF(A.X + t * abx, A.Y + t * aby);
                            float dx = channelP.X - proj.X;
                            float dy = channelP.Y - proj.Y;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                            float segDist = (float)Math.Sqrt(ab2);
                            float arcLen = segmentArcLengths[i] + t * segDist;

                            if (dist < bestDist - 0.0001f)
                            {
                                bestDist = dist;
                                bestArcLength = arcLen;
                                bestProj = proj;
                                bestSegIdx = i;
                            }
                            else if (Math.Abs(dist - bestDist) < 0.0001f)
                            {
                                if (arcLen < bestArcLength - 0.0001f)
                                {
                                    bestDist = dist;
                                    bestArcLength = arcLen;
                                    bestProj = proj;
                                    bestSegIdx = i;
                                }
                                else if (Math.Abs(arcLen - bestArcLength) < 0.0001f && i < bestSegIdx)
                                {
                                    bestDist = dist;
                                    bestArcLength = arcLen;
                                    bestProj = proj;
                                    bestSegIdx = i;
                                }
                            }
                        }

                        if (bestSegIdx != -1 && bestDist <= HeightTolerance)
                        {
                            cutPoints.Add((
                                bestArcLength,
                                bestProj,
                                ResolveProjectedIsSafeZone(plat, bestSegIdx, bestProj)));
                        }
                    }
                }

                // 3b. 手動邊錨點切分（確保 Resolve 前節點已存在於平台拓撲）
                if (mapData.ManualEdgeAnchors != null)
                {
                    foreach (var anchor in mapData.ManualEdgeAnchors)
                    {
                        if (string.Equals(anchor.FromPlatformId, plat.Id, StringComparison.Ordinal))
                        {
                            TryAddAnchorCutPoint(cutPoints, segmentArcLengths, plat, N, anchor.FromX, anchor.FromY);
                        }

                        if (string.Equals(anchor.ToPlatformId, plat.Id, StringComparison.Ordinal))
                        {
                            TryAddAnchorCutPoint(cutPoints, segmentArcLengths, plat, N, anchor.ToX, anchor.ToY);
                        }
                    }
                }

                // 4. 排序並套用雙重條件去重融合（安全旗標採 OR，避免漏標）
                var sortedCutPoints = cutPoints.OrderBy(c => c.ArcLength).ToList();
                var mergedCutPoints = new List<(float ArcLength, PointF Position, bool IsSafeZone)>();

                const float ArcTolerance = 0.5f;
                const float PositionTolerance = 1.0f;

                foreach (var cp in sortedCutPoints)
                {
                    if (mergedCutPoints.Count == 0)
                    {
                        mergedCutPoints.Add(cp);
                    }
                    else
                    {
                        var last = mergedCutPoints[mergedCutPoints.Count - 1];
                        float arcDiff = Math.Abs(cp.ArcLength - last.ArcLength);
                        float dx = cp.Position.X - last.Position.X;
                        float dy = cp.Position.Y - last.Position.Y;
                        float posDiff = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (arcDiff <= ArcTolerance && posDiff <= PositionTolerance)
                        {
                            mergedCutPoints[mergedCutPoints.Count - 1] =
                                (last.ArcLength, last.Position, last.IsSafeZone || cp.IsSafeZone);
                        }
                        else
                        {
                            mergedCutPoints.Add(cp);
                        }
                    }
                }

                // 5. 生成地圖導航節點
                var platNodes = new List<NavNodeData>();
                foreach (var cp in mergedCutPoints)
                {
                    float qX = (float)Math.Round(cp.Position.X, 1, MidpointRounding.AwayFromZero);
                    float qY = (float)Math.Round(cp.Position.Y, 1, MidpointRounding.AwayFromZero);

                    var node = new NavNodeData
                    {
                        Id = $"{VirtualNodePrefix}plat_{plat.Id}_{Quantize(qX)}_{Quantize(qY)}",
                        X = qX,
                        Y = qY,
                        Type = "Platform",
                        PlatformId = plat.Id,
                        IsSafeZone = cp.IsSafeZone
                    };
                    platNodes.Add(node);
                    generatedNodes.Add(node);
                }

                // 6. Walk 雙向邊；安全區節點加權（通道仍可走，但偏好一般平台）
                for (int i = 0; i < platNodes.Count - 1; i++)
                {
                    var fromNode = platNodes[i];
                    var toNode = platNodes[i + 1];
                    float dx = toNode.X - fromNode.X;
                    float dy = toNode.Y - fromNode.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float cost = dist;
                    if (fromNode.IsSafeZone || toNode.IsSafeZone)
                        cost *= SafeZoneWalkCostMultiplier;

                    generatedEdges.Add(new NavEdgeData
                    {
                        FromNodeId = fromNode.Id,
                        ToNodeId = toNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = cost
                    });
                    generatedEdges.Add(new NavEdgeData
                    {
                        FromNodeId = toNode.Id,
                        ToNodeId = fromNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = cost
                    });
                }
            }

            // 步驟 C：垂直繩索 → ClimbUp / ClimbDown（成本隨高度；有平台替代時較少爬繩）
            // 設計決策：端點必須落在平台上（HeightTolerance 內）才建邊；
            // 浮空端點不自動補節點，改由編輯器驗證提示使用者修正標記。
            foreach (var rope in mapData.Ropes)
            {
                if (rope.Length < 3) continue;
                float height = Math.Abs(rope[2] - rope[1]);
                TryAddVerticalChannelEdges(
                    generatedNodes,
                    generatedEdges,
                    rope[0],
                    rope[1],
                    rope[2],
                    NavigationActionType.ClimbUp,
                    NavigationActionType.ClimbDown,
                    Math.Max(ClimbUpMinCost, height * ClimbUpCostPerPx),
                    Math.Max(ClimbDownMinCost, height * ClimbDownCostPerPx),
                    $"ropeX:{rope[0]:F1}");
            }

            // 步驟 C2：垂直跳點 → Jump / JumpDown（幾何 SSOT，比照繩索）
            foreach (var link in mapData.JumpLinks ?? new List<float[]>())
            {
                if (link.Length < 3) continue;
                float height = Math.Abs(link[2] - link[1]);
                TryAddVerticalChannelEdges(
                    generatedNodes,
                    generatedEdges,
                    link[0],
                    link[1],
                    link[2],
                    NavigationActionType.Jump,
                    NavigationActionType.JumpDown,
                    Math.Max(JumpUpMinCost, height * JumpUpCostPerPx),
                    Math.Max(JumpDownMinCost, height * JumpDownCostPerPx),
                    $"jumpLinkX:{link[0]:F1}");
            }

            // 確保每次生成的節點與邊排序穩定，防止因集合順序造成 Snapshot Diff 漂移
            var sortedNodes = generatedNodes.OrderBy(n => n.Id).ToList();
            var sortedEdges = generatedEdges.OrderBy(e => e.FromNodeId).ThenBy(e => e.ToNodeId).ToList();

            mapData.Nodes.AddRange(sortedNodes);
            mapData.Edges.AddRange(sortedEdges);
        }

        private static void TryAddAnchorCutPoint(
            List<(float ArcLength, PointF Position, bool IsSafeZone)> cutPoints,
            float[] segmentArcLengths,
            PolylinePlatformData plat,
            int vertexCount,
            float anchorX,
            float anchorY)
        {
            var anchorP = new PointF(anchorX, anchorY);
            float bestDist = float.MaxValue;
            float bestArcLength = 0f;
            PointF bestProj = PointF.Empty;
            int bestSegIdx = -1;

            for (int i = 0; i < vertexCount - 1; i++)
            {
                var a = new PointF(plat.Points[i].X, plat.Points[i].Y);
                var b = new PointF(plat.Points[i + 1].X, plat.Points[i + 1].Y);

                float abx = b.X - a.X;
                float aby = b.Y - a.Y;
                float ab2 = abx * abx + aby * aby;
                if (ab2 < 0.001f) continue;

                float apx = anchorP.X - a.X;
                float apy = anchorP.Y - a.Y;
                float t = (apx * abx + apy * aby) / ab2;
                t = Math.Max(0.0f, Math.Min(1.0f, t));

                var proj = new PointF(a.X + t * abx, a.Y + t * aby);
                float dx = anchorP.X - proj.X;
                float dy = anchorP.Y - proj.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                float segDist = (float)Math.Sqrt(ab2);
                float arcLen = segmentArcLengths[i] + t * segDist;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestArcLength = arcLen;
                    bestProj = proj;
                    bestSegIdx = i;
                }
            }

            if (bestSegIdx != -1 && bestDist <= HeightTolerance)
            {
                cutPoints.Add((
                    bestArcLength,
                    bestProj,
                    ResolveProjectedIsSafeZone(plat, bestSegIdx, bestProj)));
            }
        }

        /// <summary>
        /// 投影切點的安全旗標：兩端皆安全，或距離任一安全折點 ≤ HeightTolerance。
        /// </summary>
        private static bool ResolveProjectedIsSafeZone(
            PolylinePlatformData plat,
            int segmentIndex,
            PointF projected)
        {
            var a = plat.Points[segmentIndex];
            var b = plat.Points[segmentIndex + 1];
            if (a.IsSafeZone && b.IsSafeZone)
                return true;

            foreach (var p in plat.Points)
            {
                if (!p.IsSafeZone) continue;
                float dx = p.X - projected.X;
                float dy = p.Y - projected.Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= HeightTolerance)
                    return true;
            }

            return false;
        }

        private static IEnumerable<(float X, float TopY, float BottomY)> EnumerateVerticalChannelEndpoints(MapData mapData)
        {
            if (mapData.Ropes != null)
            {
                foreach (var rope in mapData.Ropes)
                {
                    if (rope.Length < 3) continue;
                    yield return (rope[0], rope[1], rope[2]);
                }
            }

            if (mapData.JumpLinks != null)
            {
                foreach (var link in mapData.JumpLinks)
                {
                    if (link.Length < 3) continue;
                    yield return (link[0], link[1], link[2]);
                }
            }
        }

        private static void TryAddVerticalChannelEdges(
            List<NavNodeData> generatedNodes,
            List<NavEdgeData> generatedEdges,
            float channelX,
            float topY,
            float bottomY,
            NavigationActionType lowerToUpperAction,
            NavigationActionType upperToLowerAction,
            float lowerToUpperCost,
            float upperToLowerCost,
            string metadataToken)
        {
            NavNodeData? topNode = null;
            float minTopDist = float.MaxValue;
            NavNodeData? botNode = null;
            float minBotDist = float.MaxValue;

            foreach (var n in generatedNodes)
            {
                float dxTop = n.X - channelX;
                float dyTop = n.Y - topY;
                float distTop = (float)Math.Sqrt(dxTop * dxTop + dyTop * dyTop);
                if (distTop <= HeightTolerance && distTop < minTopDist)
                {
                    minTopDist = distTop;
                    topNode = n;
                }

                float dxBot = n.X - channelX;
                float dyBot = n.Y - bottomY;
                float distBot = (float)Math.Sqrt(dxBot * dxBot + dyBot * dyBot);
                if (distBot <= HeightTolerance && distBot < minBotDist)
                {
                    minBotDist = distBot;
                    botNode = n;
                }
            }

            if (topNode == null || botNode == null || string.Equals(topNode.Id, botNode.Id, StringComparison.Ordinal))
                return;

            var meta = new List<string> { metadataToken };
            generatedEdges.Add(new NavEdgeData
            {
                FromNodeId = botNode.Id,
                ToNodeId = topNode.Id,
                ActionType = lowerToUpperAction,
                Cost = lowerToUpperCost,
                InputSequence = meta
            });
            generatedEdges.Add(new NavEdgeData
            {
                FromNodeId = topNode.Id,
                ToNodeId = botNode.Id,
                ActionType = upperToLowerAction,
                Cost = upperToLowerCost,
                InputSequence = meta
            });
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value * 10f, MidpointRounding.AwayFromZero);
        }
    }
}
