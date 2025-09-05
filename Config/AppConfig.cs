using ArtaleAI.API.Models;

namespace ArtaleAI.Config
{
    ///
    /// 應用程式的主要設定檔結構 - 重新組織版本
    ///
    public class AppConfig
    {
        public GeneralSettings General { get; set; } = new();
        public TemplateSettings Templates { get; set; } = new();
        public MonsterDownloadSettings MonsterDownload { get; set; } = new();
        public ImageProcessingSettings ImageProcessing { get; set; } = new();

        // 這些設定類別現在參考 Models 命名空間
        public ArtaleAI.Models.PartyRedBarSettings PartyRedBar { get; set; } = new();
        public OverlayStyleSettings OverlayStyle { get; set; } = new();
        public ArtaleAI.Models.TemplateMatchingSettings TemplateMatching { get; set; } = new();
        public UiSettings Ui { get; set; } = new();
        public DetectionModeSettings DetectionModes { get; set; } = new();
        public MapEditorSettings MapEditor { get; set; } = new();
        public WindowCaptureSettings WindowCapture { get; set; } = new();
        public ArtaleAI.Models.PlayerDetectionSettings PlayerDetection { get; set; } = new();
    }

    public class GeneralSettings
    {
        public string GameWindowTitle { get; set; }
        public string LastSelectedWindowName { get; set; }
        public string LastSelectedProcessName { get; set; }
        public int LastSelectedProcessId { get; set; }
        public int MinimapUpscaleFactor { get; set; }
        public decimal ZoomFactor { get; set; }
    }

    public class TemplateSettings
    {
        public MinimapTemplates Minimap { get; set; }
        public ArtaleAI.Models.MonsterDetectionSettings MonsterDetection { get; set; }
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

    public class MinimapTemplates
    {
        public double PlayerThreshold { get; set; }
        public double CornerThreshold { get; set; }
        public TemplateConfig PlayerMarker { get; set; }
        public TemplateConfig OtherPlayers { get; set; }
        public CornerTemplates Corners { get; set; }
    }

    public class CornerTemplates
    {
        public TemplateConfig TopLeft { get; set; }
        public TemplateConfig TopRight { get; set; }
        public TemplateConfig BottomLeft { get; set; }
        public TemplateConfig BottomRight { get; set; }
    }

    public class TemplateConfig
    {
        public string Path { get; set; }
    }

    /// <summary>
    /// 辨識框視覺樣式設定
    /// </summary>
    public class OverlayStyleSettings
    {
        public MonsterOverlayStyle Monster { get; set; } = new();
        public MinimapOverlayStyle Minimap { get; set; } = new();
        public PlayerOverlayStyle Player { get; set; } = new();
        public PartyRedBarOverlayStyle PartyRedBar { get; set; } = new();
        public DetectionBoxOverlayStyle DetectionBox { get; set; } = new();
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
        public int DotOffsetY { get; set; } // 向下5像素
        public int DetectionBoxWidth { get; set; }
        public int DetectionBoxHeight { get; set; }
        public double DynamicFillRateSmall { get; set; }
        public double DynamicFillRateMedium { get; set; }
    }

    /// <summary>
    ///  檢測框樣式
    /// </summary>
    public class DetectionBoxOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string BoxDisplayName { get; set; }
    }

    /// <summary>
    /// 隊友血條辨識框樣式
    /// </summary>
    public class PartyRedBarOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string RedBarDisplayName { get; set; }
    }

    /// <summary>
    /// 模板匹配參數
    /// </summary>
    public class TemplateMatchingSettings
    {
        public double DefaultNmsThreshold { get; set; }
        public int MinContourPixels { get; set; }
        public double ConfidenceThreshold { get; set; }
        public double DefaultIouThreshold { get; set; }
        public double GrayscaleConfidenceMultiplier { get; set; }
        public double BasicModeNmsThreshold { get; set; }
        public int MinTemplateWidth { get; set; }
        public int MinTemplateHeight { get; set; }
        public int MaxTemplateRatio { get; set; }
        public int ProcessedMaskMinPixels { get; set; }
        public double BasicModeDefaultNmsThreshold { get; set; }
    }

    /// <summary>
    /// 地圖編輯器配置
    /// </summary>
    public class MapEditorSettings
    {
        //  滑鼠操作參數
        public float DeletionRadius { get; set; }
        public int WaypointCircleRadius { get; set; }
        public int PreviewLineWidth { get; set; }
        //  顏色配置
        public string WaypointColor { get; set; }
        public string SafeZoneColor { get; set; }
        public string RestrictedZoneColor { get; set; }
        public string RopeColor { get; set; }
        public string PreviewColor { get; set; }
        //  繪製樣式
        public float WaypointLineWidth { get; set; }
        public float SafeZoneLineWidth { get; set; }
        public float RopeLineWidth { get; set; }
    }

    /// <summary>
    /// UI 參數設定
    /// </summary>
    public class UiSettings
    {
        public int MagnifierSize { get; set; }
        public int MagnifierOffset { get; set; }
        public int CrosshairSize { get; set; }
    }

    /// <summary>
    /// 視窗捕捉配置 - 新增
    /// </summary>
    public class WindowCaptureSettings
    {
        //  捕捉性能參數
        public int CaptureFrameRate { get; set; }
        public int CaptureDelayMs { get; set; }
        public int FramePoolSize { get; set; }
        //  穩定化參數
        public int InitialDelayMs { get; set; }
        public int RetryAttempts { get; set; }
        public int RetryDelayMs { get; set; }
        //  記憶體管理
        public bool EnableMultiThreadProtection { get; set; }
        public int MaxCacheFrames { get; set; }
    }

    /// <summary>
    /// 玩家偵測配置擴展
    /// </summary>
    public class PlayerDetectionSettings
    {
        //  血條寬度分級閾值（目前硬編碼在程式中）
        public int SmallBarWidthLimit { get; set; } = 10;
        public int MediumBarWidthLimit { get; set; } = 25;
        //  處理性能參數
        public bool EnableAsyncProcessing { get; set; } = true;
        public int ProcessingTimeoutMs { get; set; } = 1000;
        //  偵測品質參數
        public double MinAspectRatio { get; set; } = 0.1; // 最小長寬比
        public double MaxAspectRatio { get; set; } = 10.0; // 最大長寬比
    }

    /// <summary>
    /// 辨識模式配置設定
    /// </summary>
    public class DetectionModeSettings
    {
        public Dictionary<string, string> ModeMapping { get; set; }
        public Dictionary<string, string> DisplayNames { get; set; }
        public string DefaultMode { get; set; }
        public string[] DisplayOrder { get; set; }
        public Dictionary<string, string> OcclusionMappings { get; set; }
        public Dictionary<string, string> ModeDescriptions { get; set; }
        public Dictionary<string, int> PerformanceLevels { get; set; }
    }
}
