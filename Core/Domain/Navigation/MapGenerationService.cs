using System;
using System.Collections.Generic;
using System.Linq;
using ArtaleAI.Models.Map;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 地圖導航拓撲生成：將繩索資源轉為 H 型導航結構（端點打樁、垂直攀爬邊、橫向接軌）。
    /// </summary>
    public static class MapGenerationService
    {
        private const string VirtualNodePrefix = "n_v_";

        /// <summary>
        /// 在既有 <see cref="MapData.Nodes"/> / <see cref="MapData.Edges"/> 上套用 H 型繩索拓撲。
        /// 會先移除先前產生的虛擬節點（Id 前綴 n_v_）及其關聯邊，以支援重複載入與編譯。
        /// </summary>
        public static void BuildHTopology(MapData mapData)
        {
            if (mapData == null) return;

            mapData.Nodes ??= new List<NavNodeData>();
            mapData.Edges ??= new List<NavEdgeData>();
            mapData.Ropes ??= new List<float[]>();

            var virtualIds = new HashSet<string>(
                mapData.Nodes
                    .Where(n => n.Id.StartsWith(VirtualNodePrefix, StringComparison.Ordinal))
                    .Select(n => n.Id));

            if (virtualIds.Count > 0)
            {
                mapData.Nodes.RemoveAll(n => virtualIds.Contains(n.Id));
                mapData.Edges.RemoveAll(e =>
                    virtualIds.Contains(e.FromNodeId) || virtualIds.Contains(e.ToNodeId));
            }

            if (mapData.Ropes.Count == 0 || mapData.Nodes.Count == 0)
                return;

            var virtualNodes = new List<NavNodeData>();
            var additionalEdges = new List<NavEdgeData>();
            var entityNodes = mapData.Nodes.ToList();

            foreach (var rope in mapData.Ropes)
            {
                if (rope.Length < 3) continue;
                float ropeX = rope[0];
                float topY = rope[1];
                float bottomY = rope[2];

                NavNodeData GetOrCreateStakingNode(float x, float y, string subId)
                {
                    var existing = entityNodes.FirstOrDefault(n =>
                        Math.Abs(n.X - x) <= 1.0f && Math.Abs(n.Y - y) <= 1.5f);
                    if (existing != null) return existing;

                    var existingV = virtualNodes.FirstOrDefault(n =>
                        Math.Abs(n.X - x) <= 0.1f && Math.Abs(n.Y - y) <= 0.1f);
                    if (existingV != null) return existingV;

                    var vNode = new NavNodeData
                    {
                        Id = BuildDeterministicVirtualNodeId(subId, x, y),
                        X = x,
                        Y = y,
                        Type = "Platform"
                    };
                    virtualNodes.Add(vNode);
                    return vNode;
                }

                var topNode = GetOrCreateStakingNode(ropeX, topY, "top");
                var bottomNode = GetOrCreateStakingNode(ropeX, bottomY, "bot");

                const float climbCostUp = 5.0f;
                const float climbCostDown = 3.0f;

                additionalEdges.Add(new NavEdgeData
                {
                    FromNodeId = bottomNode.Id,
                    ToNodeId = topNode.Id,
                    ActionType = NavigationActionType.ClimbUp,
                    Cost = climbCostUp,
                    InputSequence = new List<string> { $"ropeX:{ropeX:F1}" }
                });

                additionalEdges.Add(new NavEdgeData
                {
                    FromNodeId = topNode.Id,
                    ToNodeId = bottomNode.Id,
                    ActionType = NavigationActionType.ClimbDown,
                    Cost = climbCostDown,
                    InputSequence = new List<string> { $"ropeX:{ropeX:F1}" }
                });

                void BridgeToNearestPlatform(NavNodeData vNode)
                {
                    var nearestEntity = entityNodes
                        .Where(n => Math.Abs(n.Y - vNode.Y) <= 10.0f && n.Id != vNode.Id)
                        .OrderBy(n => Math.Abs(n.X - vNode.X))
                        .FirstOrDefault();

                    if (nearestEntity == null) return;

                    float bridgeDist = Math.Abs(nearestEntity.X - vNode.X);

                    additionalEdges.Add(new NavEdgeData
                    {
                        FromNodeId = vNode.Id,
                        ToNodeId = nearestEntity.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = bridgeDist
                    });
                    additionalEdges.Add(new NavEdgeData
                    {
                        FromNodeId = nearestEntity.Id,
                        ToNodeId = vNode.Id,
                        ActionType = NavigationActionType.Walk,
                        Cost = bridgeDist
                    });
                }

                if (virtualNodes.Contains(topNode)) BridgeToNearestPlatform(topNode);
                if (virtualNodes.Contains(bottomNode)) BridgeToNearestPlatform(bottomNode);
            }

            mapData.Nodes.AddRange(virtualNodes);
            mapData.Edges.AddRange(additionalEdges);
        }

        private static string BuildDeterministicVirtualNodeId(string subId, float x, float y)
        {
            int qx = Quantize(x);
            int qy = Quantize(y);
            return $"{VirtualNodePrefix}{subId}_{qx}_{qy}";
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value * 10f, MidpointRounding.AwayFromZero);
        }
    }
}
