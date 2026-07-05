using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ArtaleAI.Models.Map;
using ArtaleAI.Utils;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 地圖導航拓撲生成：將幾何平台與繩索資源轉為導航網格（自動切段、建立相鄰邊與爬繩邊）。
    /// </summary>
    public static class MapGenerationService
    {
        private const string VirtualNodePrefix = "n_v_";
        private const float HeightTolerance = 2.0f; // 判斷繩索點是否落在平台上的高度容許值

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
                string fromId = BuildVirtualNodeId(anchor.FromPlatformId, anchor.FromX, anchor.FromY);
                string toId = BuildVirtualNodeId(anchor.ToPlatformId, anchor.ToX, anchor.ToY);

                if (!nodeById.ContainsKey(fromId))
                {
                    Logger.Warning($"[拓撲] ResolveAnchor 失敗：找不到 fromNode {fromId}");
                    continue;
                }

                if (!nodeById.ContainsKey(toId))
                {
                    Logger.Warning($"[拓撲] ResolveAnchor 失敗：找不到 toNode {toId}");
                    continue;
                }

                bool duplicate = mapData.Edges.Any(e =>
                    string.Equals(e.FromNodeId, fromId, StringComparison.Ordinal) &&
                    string.Equals(e.ToNodeId, toId, StringComparison.Ordinal));
                if (duplicate) continue;

                float cost = ComputeAnchorCost(anchor, nodeById[fromId], nodeById[toId]);
                mapData.Edges.Add(new NavEdgeData
                {
                    FromNodeId = fromId,
                    ToNodeId = toId,
                    ActionType = anchor.ActionType,
                    Cost = cost
                });
            }
        }

        /// <summary>Cost 由 ActionType + 幾何距離決定，不由 UI 預算。</summary>
        private static float ComputeAnchorCost(ManualEdgeAnchor anchor, NavNodeData from, NavNodeData to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            return anchor.ActionType switch
            {
                NavigationActionType.Walk => dist,
                NavigationActionType.Jump => 8.0f,
                NavigationActionType.SideJump => 8.0f,
                NavigationActionType.JumpDown => 2.0f,
                NavigationActionType.Teleport => 1.0f,
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

                // 2. 初始化 cut points 集合（記錄 ArcLength 與實際座標）
                var cutPoints = new List<(float ArcLength, PointF Position)>();
                for (int i = 0; i < N; i++)
                {
                    cutPoints.Add((segmentArcLengths[i], new PointF(plat.Points[i].X, plat.Points[i].Y)));
                }

                // 3. 投影繩索端點（Rope Event Cut Points）
                foreach (var rope in mapData.Ropes)
                {
                    if (rope.Length < 3) continue;
                    float ropeX = rope[0];
                    float topY = rope[1];
                    float bottomY = rope[2];

                    var ropePoints = new PointF[] { new PointF(ropeX, topY), new PointF(ropeX, bottomY) };

                    foreach (var ropeP in ropePoints)
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

                            float apx = ropeP.X - A.X;
                            float apy = ropeP.Y - A.Y;
                            float t = (apx * abx + apy * aby) / ab2;
                            t = Math.Max(0.0f, Math.Min(1.0f, t));

                            var proj = new PointF(A.X + t * abx, A.Y + t * aby);
                            float dx = ropeP.X - proj.X;
                            float dy = ropeP.Y - proj.Y;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                            float segDist = (float)Math.Sqrt(ab2);
                            float arcLen = segmentArcLengths[i] + t * segDist;

                            // 接觸優先與 Tie-break 規則：
                            // 1. 最小投影距離優先
                            // 2. 距離相近時，累積弧長較小者優先
                            // 3. 累積弧長也相近時，Segment Index 較小者優先
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
                                else if (Math.Abs(arcLen - bestArcLength) < 0.0001f)
                                {
                                    if (i < bestSegIdx)
                                    {
                                        bestDist = dist;
                                        bestArcLength = arcLen;
                                        bestProj = proj;
                                        bestSegIdx = i;
                                    }
                                }
                            }
                        }

                        if (bestSegIdx != -1 && bestDist <= HeightTolerance)
                        {
                            cutPoints.Add((bestArcLength, bestProj));
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

                // 4. 排序並套用雙重條件去重融合
                var sortedCutPoints = cutPoints.OrderBy(c => c.ArcLength).ToList();
                var mergedCutPoints = new List<(float ArcLength, PointF Position)>();

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
                            // 同時滿足雙重條件，予以融合（保留前者）
                            continue;
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
                        Type = "Platform"
                    };
                    platNodes.Add(node);
                    generatedNodes.Add(node);
                }

                // 6. 在排序相鄰的節點之間建立 Walk 雙向邊，Cost 採實際平面歐式距離
                for (int i = 0; i < platNodes.Count - 1; i++)
                {
                    var fromNode = platNodes[i];
                    var toNode = platNodes[i + 1];
                    float dx = toNode.X - fromNode.X;
                    float dy = toNode.Y - fromNode.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    generatedEdges.Add(new NavEdgeData
                    {
                        FromNodeId = fromNode.Id,
                        ToNodeId = toNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = dist
                    });
                    generatedEdges.Add(new NavEdgeData
                    {
                        FromNodeId = toNode.Id,
                        ToNodeId = fromNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = dist
                    });
                }
            }

            // 步驟 C：建立垂直爬繩邊，使用平面歐式距離最近匹配
            foreach (var rope in mapData.Ropes)
            {
                if (rope.Length < 3) continue;
                float ropeX = rope[0];
                float topY = rope[1];
                float bottomY = rope[2];

                NavNodeData? topNode = null;
                float minTopDist = float.MaxValue;
                NavNodeData? botNode = null;
                float minBotDist = float.MaxValue;

                foreach (var n in generatedNodes)
                {
                    float dxTop = n.X - ropeX;
                    float dyTop = n.Y - topY;
                    float distTop = (float)Math.Sqrt(dxTop * dxTop + dyTop * dyTop);
                    if (distTop <= HeightTolerance && distTop < minTopDist)
                    {
                        minTopDist = distTop;
                        topNode = n;
                    }

                    float dxBot = n.X - ropeX;
                    float dyBot = n.Y - bottomY;
                    float distBot = (float)Math.Sqrt(dxBot * dxBot + dyBot * dyBot);
                    if (distBot <= HeightTolerance && distBot < minBotDist)
                    {
                        minBotDist = distBot;
                        botNode = n;
                    }
                }

                if (topNode != null && botNode != null)
                {
                    // 沿用原本 Walk 型態與 InputSequence 標註
                    generatedEdges.Add(new NavEdgeData
                    {
                        FromNodeId = botNode.Id,
                        ToNodeId = topNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = 5.0f,
                        InputSequence = new List<string> { $"ropeX:{ropeX:F1}" }
                    });
                    generatedEdges.Add(new NavEdgeData
                    {
                        FromNodeId = topNode.Id,
                        ToNodeId = botNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = 3.0f,
                        InputSequence = new List<string> { $"ropeX:{ropeX:F1}" }
                    });
                }
            }

            // 確保每次生成的節點與邊排序穩定，防止因集合順序造成 Snapshot Diff 漂移
            var sortedNodes = generatedNodes.OrderBy(n => n.Id).ToList();
            var sortedEdges = generatedEdges.OrderBy(e => e.FromNodeId).ThenBy(e => e.ToNodeId).ToList();

            mapData.Nodes.AddRange(sortedNodes);
            mapData.Edges.AddRange(sortedEdges);
        }

        private static void TryAddAnchorCutPoint(
            List<(float ArcLength, PointF Position)> cutPoints,
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
                }
            }

            if (bestDist <= HeightTolerance)
            {
                cutPoints.Add((bestArcLength, bestProj));
            }
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value * 10f, MidpointRounding.AwayFromZero);
        }
    }
}
