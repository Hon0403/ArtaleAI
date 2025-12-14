using System.Drawing;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ArtaleAI.Models.Detection
{
    /// <summary>
    /// 統一檢測結果記錄
    /// 用於儲存怪物或其他物件的檢測資訊
    /// </summary>
    public record DetectionResult(
        string Name,
        SdPoint Position,
        SdSize Size,
        double Confidence,
        Rectangle BoundingBox
    );
}
