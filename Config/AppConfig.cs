using System.Collections.Generic;
using YamlDotNet.Serialization;

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

    public class MonsterDetectionSettings
    {
        public double DefaultThreshold { get; set; }
        public double DefaultConfidence { get; set; }
        public int MaxDetectionResults { get; set; }
        public bool EnableDebugOutput { get; set; }
        public bool UseColorFilter { get; set; }
        public double ColorTolerance { get; set; }


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
}
