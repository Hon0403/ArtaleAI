using System.Collections.Generic;
using System.Drawing;
using ArtaleAI.Models.Detection;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>單幀偵測與小地圖追蹤結果快照。</summary>
    public class FrameProcessingResult
    {
        public List<SdRect> BloodBars { get; init; } = new();
        public List<SdRect> DetectionBoxes { get; init; } = new();
        public List<SdRect> AttackRangeBoxes { get; init; } = new();
        public List<DetectionResult> Monsters { get; init; } = new();
        public List<SdRect> MinimapBoxes { get; init; } = new();
        public List<SdRect> MinimapMarkers { get; init; } = new();
        public PlayerVitalsSnapshot? PlayerVitals { get; init; }
        public string? StatusMessage { get; set; }
    }
}
