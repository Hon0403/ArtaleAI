using ArtaleAI.Config;

namespace ArtaleAI
{
    /// <summary>
    /// 配置管理事件處理介面
    /// </summary>
    public interface IConfigEventHandler
    {
        /// <summary>
        /// 當配置檔案載入完成時觸發
        /// </summary>
        void OnConfigLoaded(AppConfig config);

        /// <summary>
        /// 當配置檔案儲存完成時觸發
        /// </summary>
        void OnConfigSaved(AppConfig config);

        /// <summary>
        /// 當配置操作發生錯誤時觸發
        /// </summary>
        void OnConfigError(string errorMessage);
    }
}
