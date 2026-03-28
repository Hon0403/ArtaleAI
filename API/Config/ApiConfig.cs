using System;
using ArtaleAI.API.Models;

namespace ArtaleAI.API.Config
{
    /// <summary>怪物圖下載與影像處理的靜態預設設定。</summary>
    public static class ApiConfig
    {
        public static string BaseUrl { get; private set; } = "https://maplestory.io/api/cms/361/";
        
        public static MonsterDownloadSettings MonsterDownload { get; private set; } = new MonsterDownloadSettings();
        public static ImageProcessingSettings ImageProcessing { get; private set; } = new ImageProcessingSettings();

        /// <summary>使用預設值初始化 API 配置</summary>
        public static void Initialize() => Initialize("https://maplestory.io", "");

        /// <summary>初始化 API 配置</summary>
        public static void Initialize(string baseUrl, string version)
        {
            if (!string.IsNullOrEmpty(baseUrl))
            {
                BaseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
            }
            
            MonsterDownload = new MonsterDownloadSettings
            {
                BaseUrl = "https://maplestory.io",
                DefaultRegion = "GMS",
                DefaultVersion = 65,
                TimeoutSeconds = 30,
                MaxRetryAttempts = 3,
                SkipDeathAnimations = true,
                SupportedImageFormats = new[] { "png", "jpg", "jpeg", "bmp" }
            };
            
            ImageProcessing = new ImageProcessingSettings
            {
                ConvertTransparentPixels = true,
                ReplacementColorRgb = "0,255,0",
                PreserveAlphaChannel = false
            };
        }
    }
}
