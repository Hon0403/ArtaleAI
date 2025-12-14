using System.Drawing;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Models.Minimap
{
    /// <summary>
    /// 小地圖檢測結果記錄
    /// 包含小地圖圖像、玩家位置和螢幕位置資訊
    /// </summary>
    public record MinimapResult(
        Bitmap MinimapImage,
        SdPoint? PlayerPosition,
        GraphicsCaptureItem CaptureItem,
        Rectangle? MinimapScreenRect
    );
}
