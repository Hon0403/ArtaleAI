namespace ArtaleAI
{
    /// <summary>
    /// 地圖檔案管理事件處理介面
    /// </summary>
    public interface IMapFileEventHandler
    {
        /// <summary>
        /// 顯示狀態訊息
        /// </summary>
        void OnStatusMessage(string message);

        /// <summary>
        /// 顯示錯誤訊息
        /// </summary>
        void OnError(string errorMessage);

        /// <summary>
        /// 獲取地圖資料目錄路徑
        /// </summary>
        string GetMapDataDirectory();

        /// <summary>
        /// 當地圖檔案載入完成時觸發
        /// </summary>
        void OnMapLoaded(string mapFileName);

        /// <summary>
        /// 當地圖檔案儲存完成時觸發
        /// </summary>
        void OnMapSaved(string mapFileName, bool isNewFile);

        /// <summary>
        /// 當建立新地圖時觸發
        /// </summary>
        void OnNewMapCreated();

        /// <summary>
        /// 更新視窗標題
        /// </summary>
        void UpdateWindowTitle(string title);

        /// <summary>
        /// 刷新小地圖顯示
        /// </summary>
        void RefreshMinimap();
    }
}
