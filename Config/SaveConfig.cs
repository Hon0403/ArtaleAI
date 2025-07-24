using System;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Config
{
    /// <summary>
    /// 負責儲存應用程式配置檔案
    /// </summary>
    public static class ConfigSaver
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

        public static void SaveConfig(AppConfig config, string? path = null)
        {
            var configPath = path ?? DefaultPath;

            try
            {
                Console.WriteLine($"儲存配置檔案到: {configPath}");
                Console.WriteLine($"儲存內容 - LastSelectedWindowName: '{config.General.LastSelectedWindowName}'");
                Console.WriteLine($"儲存內容 - LastSelectedProcessName: '{config.General.LastSelectedProcessName}'");

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yamlContent = serializer.Serialize(config);

                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"已建立目錄: {directory}");
                }

                File.WriteAllText(configPath, yamlContent, Encoding.UTF8);
                Console.WriteLine($"設定已成功儲存至: {configPath}");

                // 驗證寫入結果
                var reloadedConfig = ConfigLoader.LoadConfig(configPath);
                if (reloadedConfig.General.LastSelectedWindowName == config.General.LastSelectedWindowName)
                {
                    Console.WriteLine("儲存驗證成功");
                }
                else
                {
                    Console.WriteLine("儲存驗證失敗");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"儲存設定檔時發生錯誤: {ex.Message}");
                Console.WriteLine($"詳細錯誤: {ex.StackTrace}");
                throw;
            }
        }
    }
}
