namespace ArtaleAI.Models.Detection
{
    /// <summary>隊友血條檢測的視覺化顯示樣式。</summary>
    public class PartyRedBarStyle
    {
        public string FrameColor { get; set; } = "0,255,0";
        public string TextColor { get; set; } = "255,255,255";
        public int FrameThickness { get; set; }
        public string RedBarDisplayName { get; set; } = "HP";
        /// <summary>即時畫面是否繪製血條搜尋區（虛線框）。</summary>
        public bool ShowSearchRoi { get; set; } = true;
        public string SearchRoiFrameColor { get; set; } = "255,0,255";
    }

    /// <summary>通用檢測框的視覺化顯示樣式。</summary>
    public class DetectionBoxStyle
    {
        public string FrameColor { get; set; } = "255,255,0";
        public string TextColor { get; set; } = "255,255,255";
        public int FrameThickness { get; set; }
        public string BoxDisplayName { get; set; } = "Box";
    }

    /// <summary>攻擊範圍框的大小、位置和顯示樣式。</summary>
    public class AttackRangeStyle
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public string FrameColor { get; set; } = "255,0,0";
        public string TextColor { get; set; } = "255,255,255";
        public int FrameThickness { get; set; }
        public string RangeDisplayName { get; set; } = "ATK";
    }

    /// <summary>玩家底部 UI 血魔條除錯疊加樣式。</summary>
    public class PlayerVitalsStyle
    {
        public string HpFrameColor { get; set; } = "255,0,80";
        public string MpFrameColor { get; set; } = "0,255,255";
        public string TextColor { get; set; } = "255,255,0";
        public int FrameThickness { get; set; } = 3;
        public bool ShowUiBand { get; set; } = true;
        public string UiBandFrameColor { get; set; } = "255,0,255";
    }
}
