using System;
using System.Collections.Generic;
using System.Drawing;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>爬繩邊的繩 X 解析、繩段幾何判定，與掛繩途中改爬方向。</summary>
    public static class NavigationRopeHelper
    {
        /// <summary>從 Climb 邊 <c>InputSequence</c> 解析 ropeX；缺標記時回傳 false。</summary>
        public static bool TryExtractRopeX(NavigationEdge edge, out float ropeX)
        {
            ropeX = 0f;
            if (edge.InputSequence == null)
                return false;

            foreach (var seq in edge.InputSequence)
            {
                if (seq.StartsWith("ropeX:", StringComparison.Ordinal) &&
                    float.TryParse(seq.AsSpan(6), out ropeX))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 玩家是否仍在繩段「內部」垂直區間（不可執行水平 Walk）。
        /// 上下端點平台落點帶排除，避免 Climb 落地後被誤判為掛繩。
        /// </summary>
        public static bool IsPositionOnRope(
            PointF playerPos,
            IReadOnlyList<(float X, float TopY, float BottomY)> ropeSegments,
            float ropeXTolerancePx,
            float endpointYTolerancePx)
        {
            return TryGetContainingRopeSegment(
                playerPos,
                ropeSegments,
                ropeXTolerancePx,
                endpointYTolerancePx,
                out _);
        }

        /// <summary>若座標落在繩段內部，回傳該段；端點落點帶排除與 <see cref="IsPositionOnRope"/> 相同。</summary>
        public static bool TryGetContainingRopeSegment(
            PointF playerPos,
            IReadOnlyList<(float X, float TopY, float BottomY)> ropeSegments,
            float ropeXTolerancePx,
            float endpointYTolerancePx,
            out (float X, float TopY, float BottomY) segment)
        {
            segment = default;
            foreach (var candidate in ropeSegments)
            {
                if (Math.Abs(playerPos.X - candidate.X) > ropeXTolerancePx)
                    continue;

                float minY = Math.Min(candidate.TopY, candidate.BottomY);
                float maxY = Math.Max(candidate.TopY, candidate.BottomY);
                float innerMinY = minY + endpointYTolerancePx;
                float innerMaxY = maxY - endpointYTolerancePx;
                if (innerMinY > innerMaxY)
                    continue;

                if (playerPos.Y >= innerMinY && playerPos.Y <= innerMaxY)
                {
                    segment = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 掛繩途中：依目標 Y 相對角色 Y 選 ClimbUp／ClimbDown（小地圖 Y 向下為正）。
        /// 候選邊須帶 <c>ropeX:</c> 且對應當前繩段。
        /// </summary>
        public static bool TryPickClimbTowardGoal(RopeClimbPickQuery query, out RopeClimbPickResult result)
        {
            result = default;
            if (query.Candidates == null || query.Candidates.Count == 0)
                return false;

            if (!TryGetContainingRopeSegment(
                    query.PlayerPos,
                    query.RopeSegments,
                    query.RopeXTolerancePx,
                    query.EndpointYTolerancePx,
                    out var segment))
                return false;

            NavigationActionType desired = ResolveClimbDirection(query.PlayerPos.Y, query.GoalPos.Y);
            if (TryFindCandidate(query.Candidates, segment.X, query.RopeXTolerancePx, desired, out result))
                return true;

            // 缺單向邊時退另一向，避免掛繩空轉。
            var fallback = desired == NavigationActionType.ClimbUp
                ? NavigationActionType.ClimbDown
                : NavigationActionType.ClimbUp;
            return TryFindCandidate(query.Candidates, segment.X, query.RopeXTolerancePx, fallback, out result);
        }

        /// <summary>目標 Y 較小＝上方 → ClimbUp；較大＝下方 → ClimbDown。</summary>
        public static NavigationActionType ResolveClimbDirection(float playerY, float goalY)
        {
            const float tieEpsilonPx = 0.5f;
            if (goalY < playerY - tieEpsilonPx)
                return NavigationActionType.ClimbUp;
            if (goalY > playerY + tieEpsilonPx)
                return NavigationActionType.ClimbDown;

            // Y 幾乎相同：保守往上離開繩中段（多數卡點目標在上層平台）。
            return NavigationActionType.ClimbUp;
        }

        private static bool TryFindCandidate(
            IReadOnlyList<ClimbEdgeCandidate> candidates,
            float ropeX,
            float ropeXTolerancePx,
            NavigationActionType action,
            out RopeClimbPickResult result)
        {
            result = default;
            foreach (var candidate in candidates)
            {
                if (candidate.Edge.ActionType != action)
                    continue;
                if (!TryExtractRopeX(candidate.Edge, out float edgeRopeX))
                    continue;
                if (Math.Abs(edgeRopeX - ropeX) > ropeXTolerancePx)
                    continue;

                result = new RopeClimbPickResult(candidate.Edge, candidate.LandingPos);
                return true;
            }

            return false;
        }
    }

    /// <summary>圖上單條 Climb 邊與其落地座標。</summary>
    public readonly record struct ClimbEdgeCandidate(NavigationEdge Edge, PointF LandingPos);

    /// <summary>掛繩改爬查詢：角色／目標／繩段／候選 Climb 邊。</summary>
    public sealed class RopeClimbPickQuery
    {
        public required PointF PlayerPos { get; init; }
        public required PointF GoalPos { get; init; }
        public required IReadOnlyList<(float X, float TopY, float BottomY)> RopeSegments { get; init; }
        public required IReadOnlyList<ClimbEdgeCandidate> Candidates { get; init; }
        public float RopeXTolerancePx { get; init; }
        public float EndpointYTolerancePx { get; init; }
    }

    /// <summary>掛繩改爬結果。</summary>
    public readonly record struct RopeClimbPickResult(NavigationEdge Edge, PointF LandingPos);
}
