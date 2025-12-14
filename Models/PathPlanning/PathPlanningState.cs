using System;
using System.Collections.Generic;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Models.PathPlanning
{
    /// <summary>
    /// 路徑規劃狀態
    /// 追蹤角色在規劃路徑上的當前進度和狀態
    /// </summary>
    public class PathPlanningState
    {
        /// <summary>規劃的路徑點列表</summary>
        public List<SdPoint> PlannedPath { get; set; } = new();
        
        /// <summary>當前路徑點索引（從0開始）</summary>
        public int CurrentWaypointIndex { get; set; }
        
        /// <summary>路徑是否已完成</summary>
        public bool IsPathCompleted { get; set; }
        
        /// <summary>到下一個路徑點的距離（像素）</summary>
        public double DistanceToNextWaypoint { get; set; }
        
        /// <summary>玩家當前位置（使用浮點數座標提升精度）</summary>
        public SdPointF? CurrentPlayerPosition { get; set; }
        
        /// <summary>最後更新時間</summary>
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// 取得下一個要前往的路徑點
        /// </summary>
        public SdPoint? NextWaypoint
        {
            get
            {
                if (PlannedPath == null || CurrentWaypointIndex >= PlannedPath.Count)
                    return null;
                return PlannedPath[CurrentWaypointIndex];
            }
        }

        /// <summary>
        /// 臨時目標點（用於靈活路徑規劃，優先於 NextWaypoint）
        /// </summary>
        public SdPoint? TemporaryTarget { get; set; }
    }
}
