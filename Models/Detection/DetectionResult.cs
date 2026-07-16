using System.Drawing;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ArtaleAI.Models.Detection
{
    /// <summary>統一檢測結果記錄。</summary>
    public record DetectionResult(
        string Name,
        SdPoint Position,
        SdSize Size,
        double Confidence,
        Rectangle BoundingBox
    );
}
