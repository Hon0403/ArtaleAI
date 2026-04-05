using System.Collections.Generic;
using SdPointF = System.Drawing.PointF;
using SdRectF = System.Drawing.RectangleF;

namespace ArtaleAI.Models.Visualization
{
    /// <summary>供 MinimapViewer 繪製路徑、繩索與診斷標記之 DTO。</summary>
    public class PathVisualizationData
    {
        /// <summary>路徑點列表（帶優先級，用於熱力圖）</summary>
        public List<WaypointWithPriority>? WaypointPaths { get; set; }

        /// <summary>繩索列表（帶可達性資訊）</summary>
        public List<RopeWithAccessibility>? Ropes { get; set; }


        /// <summary>玩家位置（小地圖相對座標）</summary>
        public SdPointF? PlayerPosition { get; set; }

        /// <summary>目標位置（小地圖相對座標）</summary>
        public SdPointF? TargetPosition { get; set; }

        /// <summary>臨時目標位置 (動作點/中間點) - 青色十字</summary>
        public SdPointF? TemporaryTarget { get; set; }


        /// <summary>動態插值點列表 - 亮綠色小點+連線</summary>
        public List<SdPointF>? IntermediatePoints { get; set; }

        /// <summary>SSOT 目前目標 Hitbox（小地圖相對座標）</summary>
        public SdRectF? TargetHitbox { get; set; }

        /// <summary>玩家是否位於目標 Hitbox 內（SSOT）</summary>
        public bool? IsPlayerInsideTargetHitbox { get; set; }

        /// <summary>繩索對位帶中心 X（小地圖相對座標）</summary>
        public float? RopeAlignCenterX { get; set; }

        /// <summary>繩索對位容許量（像素）</summary>
        public float? RopeAlignTolerance { get; set; }

        /// <summary>目前導航動作（供診斷層文字顯示）</summary>
        public string? CurrentAction { get; set; }
    }

    /// <summary>帶優先級之路徑點（熱力圖可視化）。</summary>
    public class WaypointWithPriority
    {
        public SdPointF Position { get; set; }
        public float Priority { get; set; }
        public bool IsBlacklisted { get; set; }
        public bool IsCurrentTarget { get; set; }

        public WaypointWithPriority(SdPointF position, float priority = 0, bool isBlacklisted = false, bool isCurrentTarget = false)
        {
            Position = position;
            Priority = priority;
            IsBlacklisted = isBlacklisted;
            IsCurrentTarget = isCurrentTarget;
        }
    }

    /// <summary>帶可達性與距離資訊之繩索（可視化用）。</summary>
    public class RopeWithAccessibility
    {
        public float X { get; set; }
        public float TopY { get; set; }
        public float BottomY { get; set; }
        public float DistanceToPlayer { get; set; }
        public bool IsPlayerOnRope { get; set; }
        public bool IsTargetRope { get; set; }

        public RopeWithAccessibility(float x, float topY, float bottomY, float distance = 0, bool isOnRope = false, bool isTarget = false)
        {
            X = x;
            TopY = topY;
            BottomY = bottomY;
            DistanceToPlayer = distance;
            IsPlayerOnRope = isOnRope;
            IsTargetRope = isTarget;
        }
    }
}
