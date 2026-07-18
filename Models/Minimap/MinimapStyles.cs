namespace ArtaleAI.Models.Minimap
{
    public class MinimapStyle
    {
        public string FrameColor { get; set; } = "0,255,255";
        public string TextColor { get; set; } = "255,255,255";
        public int FrameThickness { get; set; } = 2;
        public string MinimapDisplayName { get; set; } = "Minimap";

        /// <summary>即時顯示小地圖搜尋百分比 ROI（虛線框）。</summary>
        public bool ShowSearchRoi { get; set; } = true;

        /// <summary>搜尋 ROI 框線色（R,G,B）。</summary>
        public string SearchRoiFrameColor { get; set; } = "255,165,0";
    }

    public class MinimapPlayerStyle
    {
        public string FrameColor { get; set; } = "0,255,0";
        public int FrameThickness { get; set; } = 2;
        /// <summary>小地圖自己標記色；字串為 R,G,B（歷史命名含 Bgr，與 OpenCV 通道順序不同）。</summary>
        public string PlayerColorBgr { get; set; } = "255,255,0";
        public int ColorTolerance { get; set; } = 5;
        public int MinPixelCount { get; set; } = 4;
        public float OffsetY { get; set; }
    }

    /// <summary>小地圖其他玩家標記：顏色分割（與自己玩家同一套路，色碼必須分開）。</summary>
    public class MinimapOtherPlayerStyle
    {
        /// <summary>其他玩家標記色；字串為 R,G,B（與 <see cref="MinimapPlayerStyle.PlayerColorBgr"/> 相同慣例）。</summary>
        public string OtherPlayerColorBgr { get; set; } = "255,0,0";
        public int ColorTolerance { get; set; } = 40;
        public int MinPixelCount { get; set; } = 4;
        public float OffsetY { get; set; }
        /// <summary>單幀最多回報幾個其他玩家點，避免噪點灌爆。</summary>
        public int MaxDetectCount { get; set; } = 8;
    }
}
