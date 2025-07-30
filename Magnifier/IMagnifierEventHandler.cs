using System.Drawing;

namespace ArtaleAI.Magnifier
{
    /// <summary>
    /// 浮動放大鏡事件處理介面
    /// </summary>
    public interface IMagnifierEventHandler
    {
        /// <summary>
        /// 獲取來源圖像
        /// </summary>
        Bitmap? GetSourceImage();

        /// <summary>
        /// 獲取放大倍率
        /// </summary>
        decimal GetZoomFactor();

        /// <summary>
        /// 將滑鼠座標轉換為圖像座標
        /// </summary>
        Point? ConvertToImageCoordinates(Point mouseLocation);
    }
}
