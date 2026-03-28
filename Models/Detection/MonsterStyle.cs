namespace ArtaleAI.Models.Detection
{
    /// <summary>
    /// 怪物檢測框樣式設定
    /// 定義怪物檢測結果的視覺化顯示樣式
    /// </summary>
    public class MonsterStyle
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
        
        /// <summary>是否顯示信心度</summary>
        public bool ShowConfidence { get; set; } 
        
        /// <summary>文字格式化字串</summary>
        public string TextFormat { get; set; } 
    }
}
