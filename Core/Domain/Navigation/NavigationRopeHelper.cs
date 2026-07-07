using System;
using System.Collections.Generic;
using System.Drawing;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>爬繩邊的繩 X 解析與繩段幾何判定。</summary>
    public static class NavigationRopeHelper
    {
        public static float ExtractRopeX(NavigationEdge edge, float fallbackX)
        {
            if (edge.InputSequence == null) return fallbackX;
            foreach (var seq in edge.InputSequence)
            {
                if (seq.StartsWith("ropeX:", StringComparison.Ordinal) &&
                    float.TryParse(seq.AsSpan(6), out float ropeX))
                    return ropeX;
            }
            return fallbackX;
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
            foreach (var (ropeX, topY, bottomY) in ropeSegments)
            {
                if (Math.Abs(playerPos.X - ropeX) > ropeXTolerancePx)
                    continue;

                float minY = Math.Min(topY, bottomY);
                float maxY = Math.Max(topY, bottomY);
                float innerMinY = minY + endpointYTolerancePx;
                float innerMaxY = maxY - endpointYTolerancePx;
                if (innerMinY > innerMaxY)
                    continue;

                if (playerPos.Y >= innerMinY && playerPos.Y <= innerMaxY)
                    return true;
            }

            return false;
        }
    }
}
