using System;

namespace ArtaleAI.Models.Config
{
    /// <summary>
    /// 導航與路徑規劃設定
    /// </summary>
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

        /// <summary>路徑點到達判定距離（像素）- 預設對齊 3x3 Hitbox 心臟地帶</summary>
        public double WaypointReachDistance { get; set; } = 1.5;

        /// <summary>
        /// 方向跳／側跳著陸後與目標節點 X 的容許偏差下限（像素）。
        /// 實際閾值為與 <see cref="WaypointReachDistance"/> 的較大者；實測曾出現 landDx≈6.2 且 Y 正確。
        /// </summary>
        public double JumpLandingTolerancePx { get; set; } = 7.0;

        /// <summary>
        /// 側跳前按住方向鍵的毫秒數（再起跳鍵）；略增可強化水平初速，緩解「起跳點略偏左時直接落底層」。
        /// </summary>
        public int DirectionalJumpDirectionHoldBeforeAltMs { get; set; } = 110;

        /// <summary>側跳時跳躍鍵（Alt）按住毫秒數；過短易造成小跳落層，過長依遊戲可能變攀爬。</summary>
        public int DirectionalJumpAltHoldMs { get; set; } = 85;

        /// <summary>
        /// WaitForLanding 判定「接近目標層」的 |Y−targetY| 上限（像素）。舊版硬編碼 2.5 曾使 153 vs target 150.4（差 2.6）永遠無法觸發穩定著陸。
        /// </summary>
        public double DirectionalJumpLandingYProximityPx { get; set; } = 3.2;

        /// <summary>側跳著陸等候逾時（毫秒），逾時仍會進入驗收；過短易在滯空中提早放方向鍵。</summary>
        public int DirectionalJumpLandingWaitTimeoutMs { get; set; } = 2500;

        /// <summary>
        /// WaitForLanding：連續幀 Y 視為「穩定」的最大變化（像素）。過嚴（如 0.2）時著陸後 150.4↔151.1 滑動會永遠無法累積 stableCounter。
        /// </summary>
        public double DirectionalJumpLandingStableYDeltaPx { get; set; } = 0.85;

        /// <summary>跳躍著地後延遲（毫秒），讓物理與視覺座標穩定再驗收／交給下一動作；對齊舊版「著陸後短暫等待」契約。</summary>
        public int PostLandingSettleMs { get; set; } = 100;

        /// <summary>起跳後至少經過此毫秒數，才接受「已著地」判定，避免滯空初期誤判導致提早放鍵／切狀態。</summary>
        public int MinAirborneMsBeforeLanding { get; set; } = 450;

        /// <summary>水平移動過衝後，反向碎步最多嘗試次數（Bang-Bang 對準 Hitbox）。</summary>
        public int OvershootCorrectionMaxTaps { get; set; } = 6;

        /// <summary>Walk 已進 Hitbox 後，與目標點 X 的額外對齊容許（像素）。≤0 表示關閉幾何微調。</summary>
        public double WalkGeometricAlignTolerancePx { get; set; } = 1.2;

        /// <summary>幾何微調最多短按次數（避免與過寬 Hitbox 早驗收疊加後仍偏離節點中心）。</summary>
        public int WalkGeometricTrimMaxTaps { get; set; } = 10;

        /// <summary>最大追蹤歷史記錄數量</summary>
        public int MaxTrackingHistory { get; set; } = 1000;

        /// <summary>是否啟用自動路徑尋找</summary>
        public bool EnableAutoPathFinding { get; set; } = true;

        /// <summary>是否啟用自動角色移動控制</summary>
        public bool EnableAutoMovement { get; set; } = true;

        /// <summary>平台邊界處理設定</summary>
        public PlatformBoundsConfig PlatformBounds { get; set; } = new();
    }

    /// <summary>
    /// 平台邊界處理設定
    /// </summary>
    public class PlatformBoundsConfig
    {
        /// <summary>緩衝區大小（像素，接近邊界時提前觸發減速）</summary>
        public double BufferZone { get; set; } = 5.0;

        /// <summary>緊急區域（像素，超出此範圍強制停止）</summary>
        public double EmergencyZone { get; set; } = 2.0;

        /// <summary>邊界事件冷卻時間（毫秒，防止反覆觸發）</summary>
        public int CooldownMs { get; set; } = 500;
    }
}
