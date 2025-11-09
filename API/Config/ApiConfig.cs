using ArtaleAI.API.Models;
using YamlDotNet.Serialization;

namespace ArtaleAI.API.Config
{
    public class ApiConfig
    {
        private static ApiConfig? _instance;
        public static ApiConfig Instance => _instance ?? throw new InvalidOperationException("ApiConfig not initialized");

        public MonsterDownloadSettings MonsterDownload { get; set; } = new();
        public ImageProcessingSettings ImageProcessing { get; set; } = new();

        public static void Initialize(string? configPath = null)
        {
            var path = configPath ?? Path.Combine("API", "apiconfig.yaml");

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var yaml = File.ReadAllText(path);
            _instance = deserializer.Deserialize<ApiConfig>(yaml);
        }
    }
}
