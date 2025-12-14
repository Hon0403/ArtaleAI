namespace ArtaleAI.Models.Detection
{
    /// <summary>
    /// 血條樣式設定
    /// 定義隊友血條檢測的視覺化顯示樣式
    /// </summary>
    public class PartyRedBarStyle
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
        
        /// <summary>血條顯示名稱</summary>
        public string RedBarDisplayName { get; set; }
    }

    /// <summary>
    /// 檢測框樣式設定
    /// 定義通用檢測框的視覺化顯示樣式
    /// </summary>
    public class DetectionBoxStyle
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
        
        /// <summary>檢測框顯示名稱</summary>
        public string BoxDisplayName { get; set; }
    }

    /// <summary>
    /// 攻擊範圍樣式設定
    /// 定義角色攻擊範圍框的大小、位置和顯示樣式
    /// </summary>
    public class AttackRangeStyle
    {
        /// <summary>攻擊範圍寬度（像素）</summary>
        public int Width { get; set; }
        
        /// <summary>攻擊範圍高度（像素）</summary>
        public int Height { get; set; }
        
        /// <summary>水平偏移量（像素）</summary>
        public int OffsetX { get; set; }
        
        /// <summary>垂直偏移量（像素）</summary>
        public int OffsetY { get; set; }
        
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
        
        /// <summary>攻擊範圍顯示名稱</summary>
        public string RangeDisplayName { get; set; }
    }

    /// <summary>
    /// 檢測模式配置
    /// 集中管理單一檢測模式的所有屬性
    /// </summary>
    public class DetectionModeConfig
    {
        /// <summary>UI 顯示名稱</summary>
        public string DisplayName { get; set; }
        
        /// <summary>遮擋處理方式</summary>
        public string Occlusion { get; set; }
        
        /// <summary>模式描述</summary>
        public string Description { get; set; }
        
        /// <summary>性能等級（1=最快, 5=最準確）</summary>
        public int PerformanceLevel { get; set; }
    }
}
