using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace ArtaleAI.Configuration
{
    public class AppConfig
    {
        public GeneralSettings General { get; set; } = new();
        public TemplateSettings Templates { get; set; } = new();
    }

    public class GeneralSettings
    {
        public string GameWindowTitle { get; set; } = string.Empty;
        public string LastSelectedWindowName { get; set; } = string.Empty;
        public string LastSelectedProcessName { get; set; } = string.Empty;
        public int LastSelectedProcessId { get; set; } = 0;
        public int MinimapUpscaleFactor { get; set; } = 1;
        public decimal ZoomFactor { get; set; } = 5;
    }

    public class TemplateSettings
    {
        public MinimapTemplates Minimap { get; set; } = new();
    }

    public class MinimapTemplates
    {
        public double PlayerThreshold { get; set; }
        public double CornerThreshold { get; set; }
        public TemplateConfig PlayerMarker { get; set; } = new();
        public TemplateConfig OtherPlayers { get; set; } = new();
        public CornerTemplates Corners { get; set; } = new();
    }

    public class CornerTemplates
    {
        public TemplateConfig TopLeft { get; set; } = new();
        public TemplateConfig TopRight { get; set; } = new();
        public TemplateConfig BottomLeft { get; set; } = new();
        public TemplateConfig BottomRight { get; set; } = new();
    }

    public class TemplateConfig
    {
        public string Path { get; set; } = string.Empty;
    }
}
