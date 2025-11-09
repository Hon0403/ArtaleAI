
namespace ArtaleAI.API.Models
{
    /// <summary>
    /// MapleStory.io API 返回的怪物資料模型
    /// </summary>
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

    /// <summary>
    /// 下載結果模型
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public int DownloadedCount { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> ProcessedFiles { get; set; } = new List<string>();
        public TimeSpan DownloadDuration { get; set; }
    }

    /// <summary>
    /// 怪物模板下載設定
    /// </summary>
    public class MonsterDownloadSettings
    {
        public string BaseUrl { get; set; }                    // https://maplestory.io
        public string DefaultRegion { get; set; }              // GMS
        public int DefaultVersion { get; set; }                // 65
        public int TimeoutSeconds { get; set; }                // 30
        public int MaxRetryAttempts { get; set; }              // 3
        public bool SkipDeathAnimations { get; set; }          // true
        public string OutputDirectory { get; set; }            // monster
        public bool ReplaceTransparentBackground { get; set; } // true
        public string BackgroundColor { get; set; }            // 0,255,0
        public string[] SupportedImageFormats { get; set; }    // [png, jpg, jpeg, bmp]
    }


    /// <summary>
    /// 圖像處理設定
    /// </summary>
    public class ImageProcessingSettings
    {
        // 是否將透明像素轉換為指定顏色
        public bool ConvertTransparentPixels { get; set; }

        // 替換顏色 (RGB)
        public string ReplacementColorRgb { get; set; }

        // 是否保留 Alpha 通道
        public bool PreserveAlphaChannel { get; set; }

        // 輸出格式
        public string OutputFormat { get; set; }

        // 壓縮品質
        public int CompressionQuality { get; set; }
    }

}
