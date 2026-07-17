using System.Drawing;
using ArtaleAI.Domain.Navigation;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>休息選點結果分類：決定 Pipeline 是前往、原地休息還是稍後重試。</summary>
    public enum RestSpotOutcome
    {
        Found,
        NoCandidates,
        Unreachable
    }

    public readonly record struct RestSpotSelection(RestSpotOutcome Outcome, NavigationNode? Node);

    /// <summary>
    /// 定時休息選點：安全折點與繩索同場比較，以 A* 路徑成本取最近「可達」者。
    /// 用圖上成本而非直線距離，避免挑到隔著斷層的假近點。
    /// </summary>
    public static class RestSpotSelector
    {
        private const float StartNodeSearchRadiusPx = 100f;

        public static RestSpotSelection Select(NavigationGraph graph, PointF playerPos)
        {
            var candidates = graph.GetAllNodes()
                .Where(IsRestCandidate)
                .ToList();

            if (candidates.Count == 0)
                return new(RestSpotOutcome.NoCandidates, null);

            var start = graph.FindNearestNode(playerPos, StartNodeSearchRadiusPx);
            if (start == null)
                return new(RestSpotOutcome.Unreachable, null);

            NavigationNode? best = null;
            float bestCost = float.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate.Id == start.Id)
                    return new(RestSpotOutcome.Found, candidate);

                var path = graph.FindPath(start.Id, candidate.Id);
                if (path == null || path.Edges.Count == 0)
                    continue;

                if (path.TotalCost < bestCost)
                {
                    bestCost = path.TotalCost;
                    best = candidate;
                }
            }

            return best == null
                ? new(RestSpotOutcome.Unreachable, null)
                : new(RestSpotOutcome.Found, best);
        }

        private static bool IsRestCandidate(NavigationNode node) =>
            (node.Type == NavigationNodeType.Platform && node.IsSafeZone)
            || node.Type == NavigationNodeType.Rope;
    }
}
