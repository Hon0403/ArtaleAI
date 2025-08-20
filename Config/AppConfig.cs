using ArtaleAI.Utils;
using System.Collections.Generic;
using Windows.Graphics.Capture;
using YamlDotNet.Serialization;
using CvPoint = OpenCvSharp.Point;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Config
{
    /// <summary>
    /// 應用程式的主要設定檔結構。
    /// 屬性皆為可為 null，以反映設定檔中可能缺少的區塊。
    /// </summary>
    public class AppConfig
    {
        public GeneralSettings? General { get; set; }
        public TemplateSettings? Templates { get; set; }
        public MonsterDownloadSettings? MonsterDownload { get; set; }
        public ImageProcessingSettings? ImageProcessing { get; set; }
        public PartyRedBarSettings? PartyRedBar { get; set; }

        public OverlayStyleSettings? OverlayStyle { get; set; }
    }

    public class GeneralSettings
    {
        public string? GameWindowTitle { get; set; }
        public string? LastSelectedWindowName { get; set; }
        public string? LastSelectedProcessName { get; set; }
        public int LastSelectedProcessId { get; set; }
        public int MinimapUpscaleFactor { get; set; }
        public decimal ZoomFactor { get; set; }
    }

    public class TemplateSettings
    {
        public MinimapTemplates? Minimap { get; set; }
        public MonsterDetectionSettings? MonsterDetection { get; set; }
    }

    /// <summary>
    /// 怪物偵測設定
    /// </summary>
    public class MonsterDetectionSettings
    {
        public double DefaultThreshold { get; set; }
        public int MaxDetectionResults { get; set; }
        public string DetectionMode { get; set; }
        public string OcclusionHandling { get; set; }
        public double[] MultiScaleFactors { get; set; }
        public int MorphologyKernelSize { get; set; }
        public int ContourBlurSize { get; set; }
        public double ContourThresholdLimit { get; set; }
        public double DynamicThresholdMultiplier { get; set; }
        public double NmsIouThreshold { get; set; }
        public int TemplateFreeKernelSize { get; set; }
        public int TemplateFreeOpenKernelSize { get; set; }
        public int MinDetectionArea { get; set; }
        public int MaxDetectionArea { get; set; }
        public double AspectRatioLimit { get; set; }
    }

    public class MonsterDetectionResult
    {
        public string MonsterName { get; set; } = string.Empty;
        public Point Location { get; set; }
        public double Confidence { get; set; }
        public int TemplateIndex { get; set; }
        public DateTime DetectionTime { get; set; }
        public Rectangle BoundingBox { get; set; }
        public string TemplateName { get; set; } = string.Empty;
    }

    public class MinimapTemplates
    {
        public double PlayerThreshold { get; set; }
        public double CornerThreshold { get; set; }
        public TemplateConfig? PlayerMarker { get; set; }
        public TemplateConfig? OtherPlayers { get; set; }
        public CornerTemplates? Corners { get; set; }
    }

    public class CornerTemplates
    {
        public TemplateConfig? TopLeft { get; set; }
        public TemplateConfig? TopRight { get; set; }
        public TemplateConfig? BottomLeft { get; set; }
        public TemplateConfig? BottomRight { get; set; }
    }

    public class TemplateConfig
    {
        public string? Path { get; set; }
    }

    /// <summary>
    /// 怪物模板下載設定
    /// </summary>
    public class MonsterDownloadSettings
    {
        public string BaseUrl { get; set; }
        public string DefaultRegion { get; set; }
        public string DefaultVersion { get; set; }
        public int TimeoutSeconds { get; set; }
        public int MaxRetryAttempts { get; set; }
        public bool SkipDeathAnimations { get; set; }
        public string OutputDirectory { get; set; }
        public bool ReplaceTransparentBackground { get; set; }
        public string BackgroundColor { get; set; }
        public string[] SupportedImageFormats { get; set; }
    }

    /// <summary>
    /// 下載結果模型
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public int DownloadedCount { get; set; }
        public int SkippedCount { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> ProcessedFiles { get; set; } = new List<string>();
        public TimeSpan DownloadDuration { get; set; }
    }

    /// <summary>
    /// 圖像處理設定
    /// </summary>
    public class ImageProcessingSettings
    {
        public bool ConvertTransparentPixels { get; set; }
        public string ReplacementColorBgr { get; set; }
        public bool PreserveAlphaChannel { get; set; }
        public string OutputFormat { get; set; }
        public int CompressionQuality { get; set; }
    }

    /// <summary>
    /// 模板匹配結果類別
    /// </summary>
    public class TemplateMatch
    {
        public Point Location { get; }
        public double Confidence { get; }
        public Rectangle BoundingBox { get; }

        public TemplateMatch(Point location, double confidence, Size templateSize)
        {
            Location = location;
            Confidence = confidence;
            BoundingBox = new Rectangle(location.X, location.Y, templateSize.Width, templateSize.Height);
        }
    }

    /// <summary>
    /// 怪物渲染資訊
    /// </summary>
    public class MonsterRenderInfo
    {
        public Point Location { get; set; }
        public Size Size { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public int TemplateIndex { get; set; }

        /// <summary>
        /// 獲取渲染矩形
        /// </summary>
        public Rectangle GetRenderRectangle()
        {
            return new Rectangle(Location.X, Location.Y, Size.Width, Size.Height);
        }

        /// <summary>
        /// 獲取中心點座標
        /// </summary>
        public Point GetCenterPoint()
        {
            return new Point(
                Location.X + Size.Width / 2,
                Location.Y + Size.Height / 2
            );
        }
    }

    /// <summary>
    /// 模板匹配模式枚舉
    /// </summary>
    public enum MonsterDetectionMode
    {
        Basic, // 基本模板匹配（保持相容性）
        ContourOnly, // 僅輪廓匹配
        Grayscale, // 灰階匹配
        Color, // 彩色匹配
        TemplateFree // 無模板自由偵測
    }

    /// <summary>
    /// 小地圖使用用途
    /// </summary>
    public enum MinimapUsage
    {
        PathEditing, // 路徑編輯使用
        LiveViewOverlay // 即時顯示疊加層使用
    }

    /// <summary>
    /// 小地圖載入結果
    /// </summary>
    public class MinimapLoadResult
    {
        public Bitmap? MinimapImage { get; set; }
        public GraphicsCaptureItem? CaptureItem { get; set; }

        public MinimapLoadResult(Bitmap? minimapImage, GraphicsCaptureItem? captureItem)
        {
            MinimapImage = minimapImage;
            CaptureItem = captureItem;
        }
    }

    /// <summary>
    /// 遮擋感知處理模式
    /// </summary>
    public enum OcclusionHandling
    {
        None, // 不處理遮擋
        MorphologyRepair, // 形態學修復
        SubRegionVoting, // 子區域投票
        DynamicThreshold, // 動態閾值
        MultiScale, // 多尺度匹配
        RobustLoss // 魯棒損失函數
    }

    /// <summary>
    /// 匹配結果類別
    /// </summary>
    public class MatchResult
    {
        public string Name { get; set; }
        public SdPoint Position { get; set; }
        public System.Drawing.Size Size { get; set; }
        public double Score { get; set; }
        public double Confidence { get; set; }
        public bool IsOccluded { get; set; } = false;
        public double OcclusionRatio { get; set; }
    }

    /// <summary>
    /// MapleStory.io API 返回的怪物資料模型
    /// </summary>
    public class Mob
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
    /// 辨識框視覺樣式設定
    /// </summary>
    public class OverlayStyleSettings
    {
        public MonsterOverlayStyle? Monster { get; set; }
        public MinimapOverlayStyle? Minimap { get; set; }
        public PlayerOverlayStyle? Player { get; set; }
        public PartyRedBarOverlayStyle? PartyRedBar { get; set; }
    }

    /// <summary>
    /// 怪物辨識框樣式
    /// </summary>
    public class MonsterOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public bool ShowConfidence { get; set; }
        public string TextFormat { get; set; }
    }

    /// <summary>
    /// 小地圖辨識框樣式
    /// </summary>
    public class MinimapOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string MinimapDisplayName { get; set; }
    }

    /// <summary>
    /// 玩家位置辨識框樣式
    /// </summary>
    public class PlayerOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string PlayerDisplayName { get; set; }
    }

    /// <summary>
    /// 隊友紅色血條偵測設定
    /// </summary>
    public class PartyRedBarSettings
    {
        // HSV 顏色範圍
        public int[] LowerRedHsv { get; set; }
        public int[] UpperRedHsv { get; set; }

        // 血條尺寸限制
        public int MinBarHeight { get; set; }
        public int MaxBarHeight { get; set; }
        public int MinBarWidth { get; set; }
        public int MaxBarWidth { get; set; }
        public int MinBarArea { get; set; }
        public double MinFillRate { get; set; }

        // 玩家位置偏移量
        public int PlayerOffsetX { get; set; }
        public int PlayerOffsetY { get; set; }

        // UI排除設定
        public int UiHeightFromBottom { get; set; }
    }

    ///
    /// 隊友血條辨識框樣式
    ///
    public class PartyRedBarOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string RedBarDisplayName { get; set; }
    }
}
