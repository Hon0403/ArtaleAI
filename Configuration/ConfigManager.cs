using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Configuration
{
    public static class ConfigManager
    {
        // ✅ 修改：使用專案主目錄的絕對路徑
        private static string GetProjectConfigPath()
        {
            // 取得當前執行檔案的目錄
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;

            // 向上尋找專案根目錄（包含 .csproj 檔案的目錄）
            var projectDir = currentDir;
            while (projectDir != null && !Directory.GetFiles(projectDir, "*.csproj").Any())
            {
                projectDir = Directory.GetParent(projectDir)?.FullName;
            }

            if (projectDir == null)
            {
                throw new DirectoryNotFoundException("找不到專案根目錄");
            }

            return Path.Combine(projectDir, "Configuration", "config.yaml");
        }

        private static readonly string DefaultPath = GetProjectConfigPath();

        public static AppConfig LoadConfig(string? path = null)
        {
            var configPath = path ?? DefaultPath;
            Console.WriteLine($"📖 讀取配置檔案: {configPath}");

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

        public static void SaveConfig(AppConfig config, string? path = null)
        {
            var configPath = path ?? DefaultPath;

            try
            {
                Console.WriteLine($"💾 儲存配置檔案到: {configPath}");
                Console.WriteLine($"儲存內容 - LastSelectedWindowName: '{config.General.LastSelectedWindowName}'");
                Console.WriteLine($"儲存內容 - LastSelectedProcessName: '{config.General.LastSelectedProcessName}'");

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yamlContent = serializer.Serialize(config);

                // 確保目錄存在
                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"已建立目錄: {directory}");
                }

                File.WriteAllText(configPath, yamlContent, Encoding.UTF8);
                Console.WriteLine($"✅ 設定已成功儲存至專案目錄: {configPath}");

                // 驗證寫入結果
                var reloadedConfig = LoadConfig(configPath);
                if (reloadedConfig.General.LastSelectedWindowName == config.General.LastSelectedWindowName)
                {
                    Console.WriteLine("✅ 儲存驗證成功");
                }
                else
                {
                    Console.WriteLine("❌ 儲存驗證失敗");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 儲存設定檔時發生錯誤: {ex.Message}");
                Console.WriteLine($"詳細錯誤: {ex.StackTrace}");
                throw;
            }
        }
    }
}
