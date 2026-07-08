using System;
using System.Collections.Generic;
using System.Drawing;
using ArtaleAI.Utils;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>爬繩邊的繩 X 解析與繩段幾何判定。</summary>
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
