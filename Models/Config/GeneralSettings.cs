using System;

namespace ArtaleAI.Models.Config
{
    public class GeneralSettings
    {
        public string GameWindowTitle { get; set; } = "";
        public string LastSelectedWindowName { get; set; } = "";
        public string LastSelectedProcessName { get; set; } = "";
        public int LastSelectedProcessId { get; set; }

        public int MinimapUpscaleFactor { get; set; } = 1;
        public decimal ZoomFactor { get; set; } = 1.0m;

        public int MagnifierSize { get; set; } = 200;
        public int MagnifierOffset { get; set; } = 20;
        public int CrosshairSize { get; set; } = 15;
    }
}
