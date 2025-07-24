using System;
using System.IO;
using System.Linq;
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
        private static string GetProjectConfigPath()
        {
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var projectDir = currentDir;

            while (projectDir != null && !Directory.GetFiles(projectDir, "*.csproj").Any())
            {
                projectDir = Directory.GetParent(projectDir)?.FullName;
            }

            if (projectDir == null)
            {
                throw new DirectoryNotFoundException("找不到專案根目錄");
            }

            return Path.Combine(projectDir, "Config", "config.yaml");
        }

        private static readonly string DefaultPath = GetProjectConfigPath();

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
