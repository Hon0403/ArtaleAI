using System;
using System.IO;

namespace ArtaleAI.Config
{
    public class ConfigurationManager
    {
        private readonly IConfigEventHandler _eventHandler;
        public AppConfig? CurrentConfig { get; private set; }

        public ConfigurationManager(IConfigEventHandler eventHandler)
        {
            _eventHandler = eventHandler;
        }

        public void Load()
        {
            try
            {
                // 依你的 ConfigLoader 設計取讀
                CurrentConfig = ConfigLoader.LoadConfig();
                _eventHandler.OnConfigLoaded(CurrentConfig!);
            }
            catch (Exception ex)
            {
                _eventHandler.OnConfigError($"讀取設定檔失敗: {ex.Message}");
                CurrentConfig = new AppConfig();
                _eventHandler.OnConfigLoaded(CurrentConfig);
            }
        }

        public void Save()
        {
            try
            {
                if (CurrentConfig != null)
                {
                    ConfigSaver.SaveConfig(CurrentConfig);
                    _eventHandler.OnConfigSaved(CurrentConfig);
                }
            }
            catch (Exception ex)
            {
                _eventHandler.OnConfigError($"儲存設定檔失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得/設定指定key的值 (可擴充)
        /// </summary>
        public object? GetValue(Func<AppConfig, object?> getter)
        {
            return CurrentConfig == null ? null : getter(CurrentConfig);
        }

        public void SetValue(Action<AppConfig> setter, bool autoSave = false)
        {
            if (CurrentConfig != null)
            {
                setter(CurrentConfig);
                if (autoSave) Save();
            }
        }
    }
}
