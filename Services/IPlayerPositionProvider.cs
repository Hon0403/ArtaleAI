using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 玩家位置資料來源的抽象介面。
    /// 架構考量：NavigationExecutor 需要即時查詢玩家位置以計算距離和方向，
    /// 但不應直接依賴 PathPlanningManager 或 GameVisionCore。
    /// 透過此介面反轉依賴方向，讓 Application 層只依賴抽象。
    /// </summary>
    public interface IPlayerPositionProvider
    {
        /// <summary>取得玩家當前位置（小地圖相對座標）</summary>
        SdPointF? GetCurrentPosition();
    }
}
