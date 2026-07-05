using System;
using System.Collections.Generic;
using System.Linq;
using ArtaleAI.Models.Map;

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
        /// 自動根據 PlatformSegmentData 與 Ropes 重建地圖導航拓撲。
        /// 本實作具備冪等性 (Idempotency)，多次重複呼叫產出的拓撲圖結構與順序將完全一致。
        /// </summary>
        public static void BuildHTopology(MapData mapData)
        {
            if (mapData == null) return;

            mapData.Nodes ??= new List<NavNodeData>();
            mapData.Edges ??= new List<NavEdgeData>();
            mapData.Ropes ??= new List<float[]>();
            var platforms = mapData.Platforms ?? new List<PlatformSegmentData>();

            // 1. 統一清理所有以 n_v_ 開頭的自動生成虛擬節點與相關有向邊
            mapData.Nodes.RemoveAll(n => n.Id.StartsWith(VirtualNodePrefix, StringComparison.Ordinal));
            mapData.Edges.RemoveAll(e => e.FromNodeId.StartsWith(VirtualNodePrefix, StringComparison.Ordinal) || 
                                         e.ToNodeId.StartsWith(VirtualNodePrefix, StringComparison.Ordinal));

            if (platforms.Count == 0) return;

            // 2. 執行平台幾何自動切分與拓撲生成
            BuildNewPlatformTopology(mapData, platforms);
        }

        private static void BuildNewPlatformTopology(MapData mapData, List<PlatformSegmentData> platforms)
        {
            var generatedNodes = new List<NavNodeData>();
            var generatedEdges = new List<NavEdgeData>();

            // 步驟 A：針對每個水平平台線段，投影繩索事件點並進行切段
            foreach (var plat in platforms)
            {
                // 端點正規化防禦：確保 X1 <= X2，防止因編輯器拖曳繪製方向不同而顛倒
                float xMin = Math.Min(plat.X1, plat.X2);
                float xMax = Math.Max(plat.X1, plat.X2);

                // 使用 SortedSet 進行排序，並將 X 座標進行 Quantize（四捨五入）去重，避免浮點數精度誤差
                var xPointsSet = new SortedSet<float>();
                xPointsSet.Add((float)Math.Round(xMin, 1, MidpointRounding.AwayFromZero));
                xPointsSet.Add((float)Math.Round(xMax, 1, MidpointRounding.AwayFromZero));

                // 搜尋所有落在該平台幾何區間內，且高度接近的繩索投影點
                foreach (var rope in mapData.Ropes)
                {
                    if (rope.Length < 3) continue;
                    float ropeX = rope[0];
                    float topY = rope[1];
                    float bottomY = rope[2];

                    bool isTopOnPlatform = Math.Abs(topY - plat.Y) <= HeightTolerance;
                    bool isBottomOnPlatform = Math.Abs(bottomY - plat.Y) <= HeightTolerance;

                    if ((isTopOnPlatform || isBottomOnPlatform) && ropeX >= xMin && ropeX <= xMax)
                    {
                        xPointsSet.Add((float)Math.Round(ropeX, 1, MidpointRounding.AwayFromZero));
                    }
                }

                // 轉換為地圖導航節點
                var platNodes = new List<NavNodeData>();
                foreach (var qX in xPointsSet)
                {
                    var node = new NavNodeData
                    {
                        Id = $"{VirtualNodePrefix}plat_{plat.Id}_{Quantize(qX)}_{Quantize(plat.Y)}",
                        X = qX,
                        Y = plat.Y,
                        Type = "Platform"
                    };
                    platNodes.Add(node);
                    generatedNodes.Add(node);
                }

                // 核心重構：僅在平台相鄰節點之間建立雙向 Walk 邊，防止產生平台內部直達捷徑
                for (int i = 0; i < platNodes.Count - 1; i++)
                {
                    var fromNode = platNodes[i];
                    var toNode = platNodes[i + 1];
                    float dist = Math.Abs(toNode.X - fromNode.X);

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

            // 步驟 B：建立垂直爬繩邊
            foreach (var rope in mapData.Ropes)
            {
                if (rope.Length < 3) continue;
                float ropeX = rope[0];
                float topY = rope[1];
                float bottomY = rope[2];

                var topNode = generatedNodes.FirstOrDefault(n => Math.Abs(n.X - ropeX) <= 1.0f && Math.Abs(n.Y - topY) <= HeightTolerance);
                var botNode = generatedNodes.FirstOrDefault(n => Math.Abs(n.X - ropeX) <= 1.0f && Math.Abs(n.Y - bottomY) <= HeightTolerance);

                if (topNode != null && botNode != null)
                {
                    // 沿用原本 Walk 型態與 InputSequence 標註，確保與現有隱式攀爬判定相容
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

            // 確保每次生成的節點與邊排序穩定，以便於進行 Snapshot Test
            var sortedNodes = generatedNodes.OrderBy(n => n.Id).ToList();
            var sortedEdges = generatedEdges.OrderBy(e => e.FromNodeId).ThenBy(e => e.ToNodeId).ToList();

            mapData.Nodes.AddRange(sortedNodes);
            mapData.Edges.AddRange(sortedEdges);
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value * 10f, MidpointRounding.AwayFromZero);
        }
    }
}
