using System.Drawing;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Models
{
    /// <summary>
    /// 模板匹配結果類別
    /// </summary>
    public class TemplateMatch
    {
        public Point Location { get; }
        public double Confidence { get; }
        public Rectangle BoundingBox { get; }

        public TemplateMatch(Point location, double confidence, Size templateSize)
        {
            Location = location;
            Confidence = confidence;
            BoundingBox = new Rectangle(location.X, location.Y, templateSize.Width, templateSize.Height);
        }
    }

    /// <summary>
    /// 匹配結果類別
    /// </summary>
    public class MatchResult
    {
        public string Name { get; set; }
        public SdPoint Position { get; set; }
        public System.Drawing.Size Size { get; set; }
        public double Score { get; set; }
        public double Confidence { get; set; }
        public bool IsOccluded { get; set; } = false;
        public double OcclusionRatio { get; set; }
    }
}
