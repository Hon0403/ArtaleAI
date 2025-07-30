using System;

namespace ArtaleAI.PathEditor
{
    /// <summary>
    /// 地圖檔案管理事件處理介面
    /// </summary>
    public interface IMapFileEventHandler
    {
        /// <summary>
        /// 獲取地圖資料夾路徑
        /// </summary>
        string GetMapDataDirectory();

        /// <summary>
        /// 當狀態訊息需要顯示時觸發
        /// </summary>
        void OnStatusMessage(string message);

        /// <summary>
        /// 當發生錯誤時觸發
        /// </summary>
        void OnError(string errorMessage);

        /// <summary>
        /// 當地圖載入完成時觸發
        /// </summary>
        void OnMapLoaded(string mapFileName);

        /// <summary>
        /// 當新地圖建立時觸發
        /// </summary>
        void OnNewMapCreated();

        /// <summary>
        /// 當地圖儲存完成時觸發
        /// </summary>
        void OnMapSaved(string mapFileName, bool isNewFile);

        /// <summary>
        /// 更新視窗標題
        /// </summary>
        void UpdateWindowTitle(string title);

        /// <summary>
        /// 觸發小地圖重繪
        /// </summary>
        void RefreshMinimap();
    }
}
