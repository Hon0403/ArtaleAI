using System.Drawing;

namespace ArtaleAI.Models.Detection
{
    /// <summary>
    /// 檢測框資料
    /// 儲存檢測結果的視覺化資訊
    /// </summary>
    public class DetectionBox
    {
        /// <summary>檢測框矩形區域</summary>
        public Rectangle Rectangle { get; set; }
        
        /// <summary>檢測標籤文字</summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>檢測信心度</summary>
        public double Confidence { get; set; }
        
        /// <summary>檢測框顏色</summary>
        public Color Color { get; set; }
    }
}
