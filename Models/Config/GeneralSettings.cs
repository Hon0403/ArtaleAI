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
        public int CrosshairSize { get; set; } = 15;
    }
}
