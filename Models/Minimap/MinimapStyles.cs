namespace ArtaleAI.Models.Minimap
{
    public class MinimapStyle
    {
        public string FrameColor { get; set; } = "0,255,255";
        public string TextColor { get; set; } = "255,255,255";
        public int FrameThickness { get; set; } = 2;
        public double TextScale { get; set; } = 1.0;
        public int TextThickness { get; set; } = 2;
        public string MinimapDisplayName { get; set; } = "Minimap";
    }

    public class MinimapPlayerStyle
    {
        public string FrameColor { get; set; } = "0,255,0";
        public string TextColor { get; set; } = "255,255,255";
        public int FrameThickness { get; set; } = 2;
        public double TextScale { get; set; } = 1.0;
        public int TextThickness { get; set; } = 2;
        public string PlayerDisplayName { get; set; } = "PLAYER";
        public string PlayerColorBgr { get; set; } = "136,255,255";
        public int ColorTolerance { get; set; } = 5;
        public int MinPixelCount { get; set; } = 4;
        public float OffsetY { get; set; }
    }

    /// <summary>
    /// 小地圖觀察窗樣式設定
    /// </summary>
    public class MinimapViewerStyle
    {
        public bool Enabled { get; set; } = true;
        public int ZoomFactor { get; set; } = 10;
        public int OffsetX { get; set; } = 0;
        public int OffsetY { get; set; } = 0;
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 400;
        public int BaseSize { get; set; } = 5;
        public bool ShowRuler { get; set; } = true;
        public int Opacity { get; set; } = 100;
    }
}
