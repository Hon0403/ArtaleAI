namespace ArtaleAI.Models.Minimap
{
    /// <summary>
    /// 小地圖樣式設定
    /// 定義小地圖邊界框的顯示樣式
    /// </summary>
    public class MinimapStyle
    {
        /// <summary>邊框顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; } 
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; }
        
        /// <summary>邊框粗細（像素）</summary>
        public int FrameThickness { get; set; } 
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; } 
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; }
        
        /// <summary>小地圖顯示名稱</summary>
        public string MinimapDisplayName { get; set; } 
    }

    /// <summary>
    /// 小地圖玩家標記樣式設定
    /// 定義小地圖上玩家標記的顯示樣式
    /// </summary>
    public class MinimapPlayerStyle
    {
        /// <summary>標記顏色（格式：R,G,B）</summary>
        public string FrameColor { get; set; } 
        
        /// <summary>文字顏色（格式：R,G,B）</summary>
        public string TextColor { get; set; } 
        
        /// <summary>標記粗細（像素）</summary>
        public int FrameThickness { get; set; } 
        
        /// <summary>文字縮放比例</summary>
        public double TextScale { get; set; }
        
        /// <summary>文字粗細</summary>
        public int TextThickness { get; set; } 
        
        /// <summary>玩家標記顯示名稱</summary>
        public string PlayerDisplayName { get; set; }
        
        /// <summary>玩家標記顏色 BGR 格式（用於顏色匹配偵測）</summary>
        public string PlayerColorBgr { get; set; } = "136,255,255";
        
        /// <summary>顏色容許誤差（0-30，值越大容許的顏色偏差越大）</summary>
        public int ColorTolerance { get; set; } = 5;
        
        /// <summary>最少像素數（至少要找到多少個像素才算有效偵測）</summary>
        public int MinPixelCount { get; set; } = 4;
    }
}
