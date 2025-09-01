using System.Drawing;
using Windows.Graphics.Capture;

namespace ArtaleAI.Models
{
    /// <summary>
    /// 小地圖載入結果
    /// </summary>
    public class MinimapLoadResult
    {
        public Bitmap? MinimapImage { get; set; }
        public GraphicsCaptureItem? CaptureItem { get; set; }

        public MinimapLoadResult(Bitmap? minimapImage, GraphicsCaptureItem? captureItem)
        {
            MinimapImage = minimapImage;
            CaptureItem = captureItem;
        }
    }

    /// <summary>
    /// 小地圖快照分析結果
    /// </summary>
    public class MinimapSnapshotResult
    {
        public Bitmap? MinimapImage { get; set; }
        public Point? PlayerPosition { get; set; }
        public GraphicsCaptureItem? CaptureItem { get; set; }
        public Rectangle? MinimapScreenRect { get; set; }
    }
}
