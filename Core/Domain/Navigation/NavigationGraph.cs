using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ArtaleAI.Models.Map;
using ArtaleAI.Utils;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 導航圖 (Navigation Graph)
    /// 管理地圖上所有的節點與連接關係，提供拓撲查詢能力
    /// </summary>
    public class NavigationGraph
    {
        private const float PlatformHitboxWidth = 3.0f;
        private const float PlatformHitboxHeight = 3.0f;
        private const float RopeHitboxWidth = 6.0f;
        private const float RopeHitboxHeight = 60.0f;

        private readonly Dictionary<string, NavigationNode> _nodes = new Dictionary<string, NavigationNode>();
        private readonly Dictionary<string, List<NavigationEdge>> _adjacencyList = new Dictionary<string, List<NavigationEdge>>();

        public string MapId { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;

        /// <summary>
        /// 從 <see cref="MapData"/> 建立導航圖，並自動套用 H 型繩索拓撲。
        /// </summary>
        public static NavigationGraph FromMapData(MapData mapData)
        {
            if (mapData == null) throw new ArgumentNullException(nameof(mapData));

            MapGenerationService.BuildHTopology(mapData);

            var graph = new NavigationGraph();

            foreach (var node in mapData.Nodes)
            {
                var navNode = new NavigationNode(node.X, node.Y)
                {
                    Id = node.Id,
                    Type = node.Type == "Rope" ? NavigationNodeType.Rope : NavigationNodeType.Platform
                };

                if (navNode.Type == NavigationNodeType.Rope)
                    navNode.Hitbox = new BoundingBox(node.X, node.Y, RopeHitboxWidth, RopeHitboxHeight);
                else
                    navNode.Hitbox = new BoundingBox(node.X, node.Y, PlatformHitboxWidth, PlatformHitboxHeight);

                graph.AddNode(navNode);
            }

            foreach (var edge in mapData.Edges)
            {
                var navEdge = new NavigationEdge(edge.FromNodeId, edge.ToNodeId, edge.ActionType, edge.Cost);
                if (edge.InputSequence?.Count > 0) navEdge.InputSequence = edge.InputSequence;
                graph.AddEdge(navEdge);
            }

            return graph;
        }

        /// <summary>
        /// 節點總數
        /// </summary>
        public int NodeCount => _nodes.Count;

        /// <summary>
        /// 邊總數
        /// </summary>
        public int EdgeCount => _adjacencyList.Values.Sum(list => list.Count);

        public void AddNode(NavigationNode node)
        {
            if (!_nodes.ContainsKey(node.Id))
            {
                _nodes[node.Id] = node;
                if (!_adjacencyList.ContainsKey(node.Id))
                {
                    _adjacencyList[node.Id] = new List<NavigationEdge>();
                }
            }
        }

        public void AddEdge(NavigationEdge edge)
        {
            if (_nodes.ContainsKey(edge.FromNodeId) && _nodes.ContainsKey(edge.ToNodeId))
            {
                if (!_adjacencyList.ContainsKey(edge.FromNodeId))
                {
                    _adjacencyList[edge.FromNodeId] = new List<NavigationEdge>();
                }
                _adjacencyList[edge.FromNodeId].Add(edge);
            }
            else
            {
                throw new ArgumentException($"Cannot add edge: Nodes {edge.FromNodeId} or {edge.ToNodeId} not found in graph.");
            }
        }

        public NavigationNode? GetNode(string id)
        {
            _nodes.TryGetValue(id, out var node);
            return node;
        }

        /// <summary>
        /// 取得指定節點的所有出邊 (Outgoing Edges)
        /// </summary>
        public IEnumerable<NavigationEdge> GetOutgoingEdges(string nodeId)
        {
            if (_adjacencyList.TryGetValue(nodeId, out var edges))
            {
                return edges;
            }
            return Enumerable.Empty<NavigationEdge>();
        }

        /// <summary>
        /// 取得兩點之間的邊 (若存在)
        /// </summary>
        public NavigationEdge? GetEdge(string fromNodeId, string toNodeId)
        {
            if (_adjacencyList.TryGetValue(fromNodeId, out var edges))
            {
                return edges.FirstOrDefault(e => e.ToNodeId == toNodeId);
            }
            return null;
        }

        /// <summary>
        /// 尋找距離指定座標最近的節點
        /// 導入非對稱空間感知機制：X軸視為廉價移動，Y軸給予極端懲罰，徹底消滅退回搜尋導致的跨層幻覺。
        /// </summary>
        /// <param name="position">世界座標</param>
        /// <param name="maxDistance">物理最大搜尋距離 (建議放寬)</param>
        public NavigationNode? FindNearestNode(System.Drawing.PointF position, float maxDistance = float.MaxValue, float sameLayerYThreshold = 15.0f, float yAxisPenaltyWeight = 10.0f)
        {
            NavigationNode? nearest = null;
            float minWeightedDistSq = float.MaxValue;

            foreach (var node in _nodes.Values)
            {
                float dx = node.Position.X - position.X;
                float dy = node.Position.Y - position.Y;

                float penaltyY = Math.Abs(dy) > sameLayerYThreshold ? dy * yAxisPenaltyWeight : dy;
                float weightedDistSq = dx * dx + penaltyY * penaltyY;

                float rawDistSq = dx * dx + dy * dy;
                if (rawDistSq > (maxDistance == float.MaxValue ? float.MaxValue : maxDistance * maxDistance)) continue;

                if (weightedDistSq < minWeightedDistSq)
                {
                    minWeightedDistSq = weightedDistSq;
                    nearest = node;
                }
            }

            return nearest;
        }

        public IEnumerable<NavigationNode> GetAllNodes() => _nodes.Values;

        /// <summary>
        /// A* 路徑搜尋演算法
        /// 尋找從起點到終點的最短路徑
        /// </summary>
        /// <param name="startNodeId">起點節點 ID</param>
        /// <param name="goalNodeId">終點節點 ID</param>
        /// <returns>最短路徑，若無法到達則返回 null</returns>
        public NavigationPath? FindPath(string startNodeId, string goalNodeId)
        {
            if (!_nodes.ContainsKey(startNodeId) || !_nodes.ContainsKey(goalNodeId))
            {
                Logger.Warning($"[A*] 起點或終點不存在: {startNodeId} -> {goalNodeId}");
                return null;
            }

            if (startNodeId == goalNodeId)
            {
                return new NavigationPath();
            }

            var goalNode = _nodes[goalNodeId];

            var openSet = new SortedSet<(float fScore, string nodeId)>(
                Comparer<(float fScore, string nodeId)>.Create((a, b) =>
                {
                    int cmp = a.fScore.CompareTo(b.fScore);
                    return cmp != 0 ? cmp : string.Compare(a.nodeId, b.nodeId, StringComparison.Ordinal);
                }));

            var gScore = new Dictionary<string, float>();
            var fScore = new Dictionary<string, float>();
            var cameFrom = new Dictionary<string, (string nodeId, NavigationEdge edge)>();
            var inOpenSet = new HashSet<string>();

            gScore[startNodeId] = 0;
            fScore[startNodeId] = Heuristic(startNodeId, goalNodeId);
            openSet.Add((fScore[startNodeId], startNodeId));
            inOpenSet.Add(startNodeId);

            while (openSet.Count > 0)
            {
                var current = openSet.Min;
                openSet.Remove(current);
                inOpenSet.Remove(current.nodeId);

                if (current.nodeId == goalNodeId)
                {
                    return ReconstructPath(cameFrom, goalNodeId);
                }

                foreach (var edge in GetOutgoingEdges(current.nodeId))
                {
                    string neighborId = edge.ToNodeId;
                    float tentativeG = gScore[current.nodeId] + edge.Cost;

                    if (!gScore.ContainsKey(neighborId) || tentativeG < gScore[neighborId])
                    {
                        cameFrom[neighborId] = (current.nodeId, edge);
                        gScore[neighborId] = tentativeG;
                        float newF = tentativeG + Heuristic(neighborId, goalNodeId);
                        fScore[neighborId] = newF;

                        if (!inOpenSet.Contains(neighborId))
                        {
                            openSet.Add((newF, neighborId));
                            inOpenSet.Add(neighborId);
                        }
                        else
                        {
                            var old = openSet.FirstOrDefault(x => x.nodeId == neighborId);
                            if (old != default)
                            {
                                openSet.Remove(old);
                                openSet.Add((newF, neighborId));
                            }
                        }
                    }
                }
            }

            Logger.Warning($"[A*] 無法找到路徑: {startNodeId} -> {goalNodeId}");
            return null;
        }

        /// <summary>
        /// 啟發式函數：歐幾里得距離
        /// </summary>
        private float Heuristic(string fromNodeId, string toNodeId)
        {
            if (!_nodes.TryGetValue(fromNodeId, out var from) ||
                !_nodes.TryGetValue(toNodeId, out var to))
            {
                return float.MaxValue;
            }

            float dx = to.Position.X - from.Position.X;
            float dy = to.Position.Y - from.Position.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 回溯建構路徑
        /// </summary>
        private NavigationPath ReconstructPath(
            Dictionary<string, (string nodeId, NavigationEdge edge)> cameFrom,
            string goalNodeId)
        {
            var edges = new List<NavigationEdge>();
            string current = goalNodeId;

            while (cameFrom.ContainsKey(current))
            {
                var (prevNode, edge) = cameFrom[current];
                edges.Add(edge);
                current = prevNode;
            }

            edges.Reverse();
            return new NavigationPath(edges);
        }
    }
}
