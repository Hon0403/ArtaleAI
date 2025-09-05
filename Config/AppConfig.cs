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

    public class OverlayStyleSettings
    {
        public MonsterOverlayStyle Monster { get; set; } = new();
        public MinimapOverlayStyle Minimap { get; set; } = new();
        public PlayerOverlayStyle Player { get; set; } = new();
        public PartyRedBarOverlayStyle PartyRedBar { get; set; } = new();
        public DetectionBoxOverlayStyle DetectionBox { get; set; } = new();
    }

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

    public class MinimapOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string MinimapDisplayName { get; set; }
    }

    public class PlayerOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string PlayerDisplayName { get; set; }
    }

    public class DetectionBoxOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string BoxDisplayName { get; set; }
    }

    public class PartyRedBarOverlayStyle
    {
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public int FrameThickness { get; set; }
        public double TextScale { get; set; }
        public int TextThickness { get; set; }
        public string RedBarDisplayName { get; set; }
    }

    public class MapEditorSettings
    {
        public float DeletionRadius { get; set; }
        public int WaypointCircleRadius { get; set; }
        public int PreviewLineWidth { get; set; }
        public string WaypointColor { get; set; }
        public string SafeZoneColor { get; set; }
        public string RestrictedZoneColor { get; set; }
        public string RopeColor { get; set; }
        public string PreviewColor { get; set; }
        public float WaypointLineWidth { get; set; }
        public float SafeZoneLineWidth { get; set; }
        public float RopeLineWidth { get; set; }
    }

    public class UiSettings
    {
        public int MagnifierSize { get; set; }
        public int MagnifierOffset { get; set; }
        public int CrosshairSize { get; set; }
    }

    public class WindowCaptureSettings
    {
        public int CaptureFrameRate { get; set; }
        public int CaptureDelayMs { get; set; }
        public int FramePoolSize { get; set; }
        public int InitialDelayMs { get; set; }
        public int RetryAttempts { get; set; }
        public int RetryDelayMs { get; set; }
        public bool EnableMultiThreadProtection { get; set; }
        public int MaxCacheFrames { get; set; }
    }

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
