namespace ArtaleAI.Application.Console
{
    /// <summary>主控台不可變 ViewModel；Form 僅負責綁定到控件。</summary>
    public sealed class ConsoleViewState
    {
        public string GameWindowLabel { get; init; } = string.Empty;
        public ConsoleStatusTone GameWindowTone { get; init; }

        public string StatusGame { get; init; } = string.Empty;
        public ConsoleStatusTone StatusGameTone { get; init; }

        public string StatusCapture { get; init; } = string.Empty;
        public ConsoleStatusTone StatusCaptureTone { get; init; }

        public string StatusFsm { get; init; } = string.Empty;
        public ConsoleStatusTone StatusFsmTone { get; init; }

        public string StatusVitals { get; init; } = string.Empty;
        public ConsoleStatusTone StatusVitalsTone { get; init; }

        public string StatusPath { get; init; } = string.Empty;
        public ConsoleStatusTone StatusPathTone { get; init; }

        public string PrerequisitesText { get; init; } = string.Empty;
        public ConsoleStatusTone PrerequisitesTone { get; init; }

        public int HpPercent { get; init; }
        public int MpPercent { get; init; }
        public bool HasVitalsReading { get; init; }
    }
}
