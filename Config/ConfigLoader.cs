using ArtaleAI.Utils;
using System;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Config
{
    /// <summary>
    /// 負責載入應用程式配置檔案
    /// </summary>
    public static class ConfigLoader
    {
        // ✅ 移除重複的 GetProjectConfigPath() 方法
        // ✅ 使用統一的工具類
        private static readonly string DefaultPath = PathUtils.GetConfigFilePath();

        public static AppConfig LoadConfig(string? path = null)
        {
            var configPath = path ?? DefaultPath;
            Console.WriteLine($"讀取配置檔案: {configPath}");

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
    }
}
