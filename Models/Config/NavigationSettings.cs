using System;

namespace ArtaleAI.Models.Config
{
    /// <summary>導航、走路與側跳相關參數。</summary>
    public class NavigationSettings
    {
        /// <summary>連續檢測間隔（毫秒）</summary>
        public int ContinuousDetectionIntervalMs { get; set; } = 100;

        /// <summary>玩家位置檢測閾值</summary>
        public double PlayerPositionThreshold { get; set; } = 0.7;

        /// <summary>其他玩家檢測閾值</summary>
        public double OtherPlayersThreshold { get; set; } = 0.5;

        /// <summary>是否啟用其他玩家檢測</summary>
        public bool EnableOtherPlayersDetection { get; set; } = true;

        /// <summary>路徑點相關距離（像素）：側跳著陸 X 與 <see cref="JumpLandingTolerancePx"/> 取大；繩索 X 對齊容差與小地圖繩線視覺半寬亦用此值（與 Hitbox.Contains 到達判定分離）。</summary>
        public double WaypointReachDistance { get; set; } = 2.0;

        /// <summary>側跳著陸 X 容差下限（px）；與 <see cref="WaypointReachDistance"/> 取較大者驗收。</summary>
        public double JumpLandingTolerancePx { get; set; } = 9.0;

        /// <summary>Alt 放開後經此毫秒即放開方向鍵；0＝舊行為（等 WaitForLanding 結束才放）。可避免落地後仍長按滑出 X 容差。</summary>
        public int SideJumpReleaseDirectionMsAfterAlt { get; set; } = 0;

        /// <summary>側跳著陸 Y 驗收容許（px）；0 則使用執行器內建 15。僅影響方向跳著陸檢查。</summary>
        public double SideJumpLandingMaxFallYPx { get; set; } = 0;

        /// <summary>側跳：按下方向鍵**之前**原地等待（ms），不揹方向避免薄邊緣先走出去。</summary>
        public int SideJumpWindupMs { get; set; } = 22;

        /// <summary>按下方向後、按下 Alt 前的極短等待（ms）；略增可拉遠跳距，過大易在邊緣滑步。</summary>
        public int SideJumpDirectionLeadMsBeforeAlt { get; set; } = 12;

        /// <summary>跳躍鍵（Alt）按住（ms）。</summary>
        public int SideJumpAltHoldMs { get; set; } = 105;

        /// <summary>同層側跳：StopMovement 後再等（ms）再接方向；0 關閉。</summary>
        public int SideJumpPreJumpSettleMs { get; set; } = 45;

        /// <summary>WaitForLanding 逾時（ms），逾時仍做座標驗收。</summary>
        public int SideJumpLandingTimeoutMs { get; set; } = 2500;

        /// <summary>水平移動過衝後反向碎步次數；0＝關閉。雪地等易過衝時改 3～6。</summary>
        public int OvershootCorrectionMaxTaps { get; set; } = 3;

        /// <summary>最大追蹤歷史記錄數量</summary>
        public int MaxTrackingHistory { get; set; } = 1000;

        /// <summary>是否啟用自動路徑尋找</summary>
        public bool EnableAutoPathFinding { get; set; } = true;

        /// <summary>是否啟用自動角色移動控制</summary>
        public bool EnableAutoMovement { get; set; } = true;
    }
}
