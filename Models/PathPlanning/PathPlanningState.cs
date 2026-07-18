using System.Collections.Generic;
using System.Drawing;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Models.PathPlanning
{
    /// <summary>路徑規劃狀態：角色在規劃路徑上的進度。</summary>
    public class PathPlanningState
    {
        public List<SdPointF> PlannedPath { get; set; } = new();
        public List<string> PlannedPathNodes { get; set; } = new();
        public int CurrentWaypointIndex { get; set; }
        public bool IsPathCompleted { get; set; }
        public double DistanceToNextWaypoint { get; set; }
        public SdPointF? CurrentPlayerPosition { get; set; }

        public SdPointF? NextWaypoint
        {
            get
            {
                if (PlannedPath == null || CurrentWaypointIndex >= PlannedPath.Count)
                    return null;
                return PlannedPath[CurrentWaypointIndex];
            }
        }

        /// <summary>臨時目標點（優先於 <see cref="NextWaypoint"/>）。</summary>
        public SdPointF? TemporaryTarget { get; set; }
    }
}
