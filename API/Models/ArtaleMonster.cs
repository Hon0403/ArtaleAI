
namespace ArtaleAI.API.Models
{
    /// <summary>MapleStory.io CMS 怪物 JSON 對應模型。</summary>
    public class ArtaleMonster
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Level { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        public int Exp { get; set; }
    }

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public int DownloadedCount { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> ProcessedFiles { get; set; } = new List<string>();
        public TimeSpan DownloadDuration { get; set; }
    }

    public class MonsterDownloadSettings
    {
        public string BaseUrl { get; set; } = "";
        public string DefaultRegion { get; set; } = "";
        public int DefaultVersion { get; set; }
        public int TimeoutSeconds { get; set; }
        public int MaxRetryAttempts { get; set; }
        public bool SkipDeathAnimations { get; set; }
        public string OutputDirectory { get; set; } = "";
        public bool ReplaceTransparentBackground { get; set; }
        public string BackgroundColor { get; set; } = "";
        public string[] SupportedImageFormats { get; set; } = Array.Empty<string>();
    }

    public class ImageProcessingSettings
    {
        public bool ConvertTransparentPixels { get; set; }
        public string ReplacementColorRgb { get; set; } = "";
        public bool PreserveAlphaChannel { get; set; }
        public string OutputFormat { get; set; } = "";
        public int CompressionQuality { get; set; }
    }

}
