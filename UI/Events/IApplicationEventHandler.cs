using System.Drawing;

namespace ArtaleAI
{
    /// <summary>
    /// 統一的應用程式事件處理介面
    /// 合併了放大鏡、怪物模板和即時顯示的相關功能
    /// </summary>
    public interface IApplicationEventHandler
    {
        #region 通用事件

        /// <summary>
        /// 顯示狀態訊息
        /// </summary>
        void OnStatusMessage(string message);

        /// <summary>
        /// 顯示錯誤訊息
        /// </summary>
        void OnError(string errorMessage);

        #endregion

        #region 放大鏡功能 (原 IMagnifierEventHandler)

        /// <summary>
        /// 獲取來源圖像 (用於放大鏡)
        /// </summary>
        Bitmap? GetSourceImage();

        /// <summary>
        /// 獲取縮放倍率
        /// </summary>
        decimal GetZoomFactor();

        /// <summary>
        /// 將滑鼠座標轉換為圖像座標
        /// </summary>
        Point? ConvertToImageCoordinates(Point mouseLocation);

        #endregion

        #region 怪物模板功能 (原 IMonsterTemplateEventHandler)

        /// <summary>
        /// 獲取怪物模板目錄路徑
        /// </summary>
        string GetMonstersDirectory();

        /// <summary>
        /// 當怪物模板載入完成時觸發
        /// </summary>
        void OnTemplatesLoaded(string monsterName, int templateCount);

        #endregion
    }
}
