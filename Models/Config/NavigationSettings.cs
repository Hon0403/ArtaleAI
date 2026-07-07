using System;

namespace ArtaleAI.Models.Config
{
    /// <summary>導航、走路與側跳核心參數 (SSOT 原子化)。</summary>
    public class NavigationSettings
    {
        /// <summary>玩家位置檢測閾值</summary>
        public double PlayerPositionThreshold { get; set; } = 0.7;

        /// <summary>其他玩家檢測閾值</summary>
        public double OtherPlayersThreshold { get; set; } = 0.5;

        /// <summary>是否啟用其他玩家檢測</summary>
        public bool EnableOtherPlayersDetection { get; set; } = true;

        /// <summary>平台節點的到達判定寬度 (px)</summary>
        public double PlatformHitboxWidth { get; set; } = 3.0;

        /// <summary>平台節點的到達判定高度 (px)</summary>
        public double PlatformHitboxHeight { get; set; } = 3.0;

        /// <summary>走路終點 X 軸精準對齊容差 (px)，與爬繩對位一致。</summary>
        public double WalkAlignTolerancePx { get; set; } = 1.0;

        /// <summary>距目標 X 此距離內釋放長按，改由微步對齊以防 overshoot。</summary>
        public double WalkBrakeDistancePx { get; set; } = 3.5;

        /// <summary>平台折線投影 Y 容差 (px)；斜坡可站高度帶。</summary>
        public double SlopeStandYTolerancePx { get; set; } = 4.5;

        /// <summary>爬繩落地 Y 容差 (px)；嚴格於斜坡帶，避免繩上誤判。</summary>
        public double RopeLandingYTolerancePx { get; set; } = 1.5;

        /// <summary>繩段 X 軸判定容差 (px)；用於繩上狀態與 RopeLanding。</summary>
        public double RopeSegmentXTolerancePx { get; set; } = 1.5;

        /// <summary>Jump approach Walk 連續 Failed 次數達此值即熔斷 Rescue。</summary>
        public int ApproachFailureRescueCutoff { get; set; } = 3;

        /// <summary>同座標、同補給節點連續救援達此值即熔斷，避免 livelock。</summary>
        public int RescueRepeatCutoff { get; set; } = 3;

        /// <summary>繩索節點的到達判定寬度 (px)</summary>
        public double RopeHitboxWidth { get; set; } = 6.0;

        /// <summary>繩索節點的到達判定高度 (px)</summary>
        public double RopeHitboxHeight { get; set; } = 15.0;

        /// <summary>跳躍鍵（Alt）按住時間 (ms)，影響跳躍弧度與高度。</summary>
        public int SideJumpAltHoldMs { get; set; } = 110;

        /// <summary>最大位移停滯閾值（毫秒）；超過此時間位移量不足引發 Rescue。</summary>
        public int StuckDetectionMs { get; set; } = 3000;

        /// <summary>是否啟用自動路徑尋找</summary>
        public bool EnableAutoPathFinding { get; set; } = true;

        /// <summary>是否啟用自動角色移動控制</summary>
        public bool EnableAutoMovement { get; set; } = true;
    }
}
