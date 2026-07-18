using System.Collections.Generic;
using SdPointF = System.Drawing.PointF;
using SdRectF = System.Drawing.RectangleF;

namespace ArtaleAI.Models.Visualization
{
    /// <summary>供主控台運行小地圖繪製路徑、繩索與診斷標記之 DTO。</summary>
    public class PathVisualizationData
    {
        public List<WaypointWithPriority>? WaypointPaths { get; set; }
        public List<RopeWithAccessibility>? Ropes { get; set; }
        public List<SdPointF>? PlannedPath { get; set; }
        public int CurrentWaypointIndex { get; set; }
        public SdPointF? PlayerPosition { get; set; }
        public SdPointF? TargetPosition { get; set; }
        public SdPointF? TemporaryTarget { get; set; }
        public SdRectF? TargetHitbox { get; set; }
        public bool? IsPlayerInsideTargetHitbox { get; set; }
        public string? CurrentAction { get; set; }
    }

    /// <summary>路徑點標記（目前目標／黑名單）。</summary>
    public class WaypointWithPriority
    {
        public SdPointF Position { get; set; }
        public bool IsBlacklisted { get; set; }
        public bool IsCurrentTarget { get; set; }

        public WaypointWithPriority(SdPointF position, bool isBlacklisted = false, bool isCurrentTarget = false)
        {
            Position = position;
            IsBlacklisted = isBlacklisted;
            IsCurrentTarget = isCurrentTarget;
        }
    }

    /// <summary>繩索可視化（是否為玩家所在繩）。</summary>
    public class RopeWithAccessibility
    {
        public float X { get; set; }
        public float TopY { get; set; }
        public float BottomY { get; set; }
        public bool IsPlayerOnRope { get; set; }

        public RopeWithAccessibility(float x, float topY, float bottomY, bool isOnRope = false)
        {
            X = x;
            TopY = topY;
            BottomY = bottomY;
            IsPlayerOnRope = isOnRope;
        }
    }
}
