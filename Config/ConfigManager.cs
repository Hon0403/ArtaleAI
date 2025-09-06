using ArtaleAI.Utils;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Config
{
    public class ConfigManager
    {
        private readonly MainForm _mainForm;
        private static readonly string DefaultPath = UtilityHelper.GetConfigFilePath();

        public AppConfig? CurrentConfig { get; private set; }

        public ConfigManager(MainForm mainForm)
        {
            _mainForm = mainForm;
        }

        #region 載入配置
        public void Load(string? path = null)
        {
            try
            {
                CurrentConfig = LoadFromFile(path);
                _mainForm.OnConfigLoaded(CurrentConfig!);
            }
            catch (Exception ex)
            {
                _mainForm.OnConfigError($"讀取設定檔失敗: {ex.Message}");
            }
        }

        private AppConfig LoadFromFile(string? path = null)
        {
            var configPath = path ?? DefaultPath;

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"找不到設定檔！路徑：{configPath}", configPath);
            }

            var yamlContent = File.ReadAllText(configPath, Encoding.UTF8);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<AppConfig>(yamlContent) ?? new AppConfig();
        }
        #endregion

        #region 儲存配置
        public void Save(string? path = null)
        {
            try
            {
                if (CurrentConfig != null)
                {
                    SaveToFile(CurrentConfig, path);
                    _mainForm.OnConfigSaved(CurrentConfig);
                }
            }
            catch (Exception ex)
            {
                _mainForm.OnConfigError($"儲存設定檔失敗: {ex.Message}");
            }
        }

        private void SaveToFile(AppConfig config, string? path = null)
        {
            var configPath = path ?? DefaultPath;

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlContent = serializer.Serialize(config);

            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configPath, yamlContent, Encoding.UTF8);
        }
        #endregion

        #region 配置操作

        public void SetValue(Action<AppConfig> setter, bool autoSave = false)
        {
            if (CurrentConfig != null)
            {
                setter(CurrentConfig);
                if (autoSave) Save();
            }
        }
        #endregion
    }
}
