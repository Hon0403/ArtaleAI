using System;
using System.ComponentModel;
using System.IO;
using ArtaleAI.Utils;
using ArtaleAI.Core.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Minimap;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Models.Config
{
    /// <summary>全域 YAML 組態（Singleton）；子區塊見 General、Vision、Navigation 等。</summary>
    public class AppConfig : INotifyPropertyChanged
    {
        private static AppConfig? _instance;
        private static readonly object _lock = new object();
        private static string _activeConfigPath = "config.yaml";

        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException("AppConfig not initialized");
                return _instance;
            }
        }

        #region 模組化子設定

        public GeneralSettings General { get; set; } = new();
        public VisionSettings Vision { get; set; } = new();
        public NavigationSettings Navigation { get; set; } = new();
        public EditorSettings Editor { get; set; } = new();
        public AppearanceSettings Appearance { get; set; } = new();

        #endregion

        #region 初始化與儲存方法

        public static void Initialize(string configPath = "config.yaml")
        {
            lock (_lock)
            {
                if (_instance != null) return;
                _activeConfigPath = Path.GetFullPath(configPath);
                try
                {
                    if (File.Exists(_activeConfigPath))
                    {
                        var yaml = File.ReadAllText(_activeConfigPath);
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();
                        _instance = deserializer.Deserialize<AppConfig>(yaml);
                        Logger.Info($"[Config] 載入設定檔: {_activeConfigPath}");
                    }
                    else
                    {
                        _instance = new AppConfig();
                        Logger.Warning($"[Config] 找不到設定檔，使用預設值: {_activeConfigPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Config 載入失敗，使用預設值: {ex.Message}");
                    _instance = new AppConfig();
                }
            }
        }

        public void Save(string configPath = "config.yaml")
        {
            try
            {
                string targetPath = string.IsNullOrWhiteSpace(configPath) || configPath == "config.yaml"
                    ? _activeConfigPath
                    : Path.GetFullPath(configPath);

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yaml = serializer.Serialize(this);
                File.WriteAllText(targetPath, yaml);
                _activeConfigPath = targetPath;
                Logger.Info($"[Config] 已儲存設定檔: {targetPath}");
                OnPropertyChanged(nameof(AppConfig));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save config: {ex.Message}", ex);
            }
        }

        #endregion

        #region INotifyPropertyChanged 實作

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
