using ArtaleAI.Config;

namespace ArtaleAI.Interfaces
{
    /// <summary>
    /// MainForm 的統一事件處理介面
    /// </summary>
    public interface IMainFormEvents
    {
        #region 通用事件
        void OnStatusMessage(string message);
        void OnError(string errorMessage);
        #endregion

        #region 配置事件
        void OnConfigLoaded(AppConfig config);
        void OnConfigSaved(AppConfig config);
        void OnConfigError(string errorMessage);

        ConfigManager? ConfigurationManager { get; }

        #endregion

        #region 即時顯示事件
        void OnFrameAvailable(Bitmap frame);
        #endregion

        #region 地圖檔案事件
        void OnMapLoaded(string mapFileName);
        void OnMapSaved(string mapFileName, bool isNewFile);
        void OnNewMapCreated();
        void UpdateWindowTitle(string title);
        void RefreshMinimap();
        string GetMapDataDirectory();
        #endregion

        #region 應用程式功能
        Bitmap? GetSourceImage();
        decimal GetZoomFactor();
        Point? ConvertToImageCoordinates(Point mouseLocation);
        string GetMonstersDirectory();
        void OnTemplatesLoaded(string monsterName, int templateCount);
        #endregion
    }
}
