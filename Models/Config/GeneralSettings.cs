using System;

namespace ArtaleAI.Models.Config
{
    public class GeneralSettings
    {
        public string GameWindowTitle { get; set; } = "";
        public string LastSelectedWindowName { get; set; } = "";
        public string LastSelectedProcessName { get; set; } = "";
        public int LastSelectedProcessId { get; set; }

        public int CrosshairSize { get; set; } = 15;

        /// <summary>擷取進行中時，持續檢查並校正遊戲客戶區尺寸。</summary>
        public bool ForceClientSizeWhileCapture { get; set; } = true;

        /// <summary>對齊 maplestory.io 原尺寸模板約 1×（16:9）。</summary>
        public int ForceClientWidth { get; set; } = 1280;
        public int ForceClientHeight { get; set; } = 720;

        /// <summary>檢查間隔（毫秒）。</summary>
        public int ForceClientSizeCheckIntervalMs { get; set; } = 1500;

        /// <summary>成功強制改尺寸後的冷卻，避免與遊戲搶拉視窗。</summary>
        public int ForceClientSizeCooldownMs { get; set; } = 2500;
    }
}
