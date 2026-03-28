using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>玩家座標來源（小地圖座標系），供導航執行層查詢。</summary>
    public interface IPlayerPositionProvider
    {
        /// <summary>目前位置；無效時回傳 null。</summary>
        SdPointF? GetCurrentPosition();
    }
}
