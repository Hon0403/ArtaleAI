using ArtaleAI.Config;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using Windows.Graphics.Capture;

namespace ArtaleAI.Models
{
    #region 小地圖相關模型

    public class MinimapSnapshot
    {
        public Bitmap MinimapImage { get; set; } = null!;
        public Point? PlayerPosition { get; set; }
        public GraphicsCaptureItem CaptureItem { get; set; } = null!;
        public Rectangle? MinimapScreenRect { get; set; }
    }

    public class MinimapLoadResult
    {
        public Bitmap MinimapImage { get; set; }
        public GraphicsCaptureItem CaptureItem { get; set; }

        public MinimapLoadResult(Bitmap minimapImage, GraphicsCaptureItem captureItem)
        {
            MinimapImage = minimapImage;
            CaptureItem = captureItem;
        }
    }

    public class MinimapSnapshotResult
    {
        public Bitmap MinimapImage { get; set; } = null!;
        public Point? PlayerPosition { get; set; }
        public GraphicsCaptureItem CaptureItem { get; set; } = null!;
        public Rectangle? MinimapScreenRect { get; set; }
    }

    #endregion

    #region 檢測結果模型

    /// <summary>
    /// 模板資料傳遞類別 - 用於跨執行緒傳遞UI資料
    /// </summary>
    public class TemplateData
    {
        public string SelectedMonsterName { get; set; }
        public List<Bitmap> Templates { get; set; }
        public string DetectionMode { get; set; }
        public double Threshold { get; set; }
        public int TemplateCount { get; set; }
    }

    public class MatchResult
    {
        public string Name { get; set; } = "";
        public Point Position { get; set; }
        public Size Size { get; set; }
        public double Score { get; set; }
        public double Confidence { get; set; }
        public bool IsOccluded { get; set; }
        public double OcclusionRatio { get; set; }
    }

    public class DetectionResult
    {
        public string Name { get; set; } = "";
        public Point Position { get; set; }
        public Size Size { get; set; }
        public double Score { get; set; }
        public double Confidence { get; set; }
    }

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

    #endregion

    #region 地圖數據模型

    /// <summary>
    /// 代表一條由多個 Waypoint 組成的連續路徑
    /// </summary>
    public class MapPath : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public List<Waypoint> Points { get; set; } = new List<Waypoint>();
    }

    /// <summary>
    /// 代表一個路徑點
    /// </summary>
    public class Waypoint : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public PointF Position { get; set; }
    }

    /// <summary>
    /// 代表一個多邊形區域（例如可行走、不可進入）
    /// </summary>
    public class MapArea : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public List<PointF> Points { get; set; } = new List<PointF>();
    }

    /// <summary>
    /// 代表一條繩索或梯子路徑
    /// </summary>
    public class Rope : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public PointF Start { get; set; }
        public PointF End { get; set; }
    }

    /// <summary>
    /// 負責管理一張地圖上的所有標記數據，並處理檔案的儲存與讀取
    /// </summary>
    public class MapData
    {
        public List<PathData>? WaypointPaths { get; set; }
        public List<PathData>? SafeZones { get; set; }
        public List<PathData>? Ropes { get; set; }
        public List<PointF>? RestrictedPoints { get; set; }
    }

    /// <summary>
    /// 統一的路徑/區域數據 - 所有模式都使用相同結構
    /// </summary>
    public class PathData
    {
        public List<PointF> Points { get; set; } = new();
    }

    #endregion

    #region 設定類別 - 統一在 Models 中管理

    public class PartyRedBarSettings
    {
        public int[] LowerRedHsv { get; set; }
        public int[] UpperRedHsv { get; set; }
        public int MinBarHeight { get; set; }
        public int MaxBarHeight { get; set; }
        public int MinBarWidth { get; set; }
        public int MaxBarWidth { get; set; }
        public int MinBarArea { get; set; }
        public double MinFillRate { get; set; }
        public int PlayerOffsetX { get; set; }
        public int PlayerOffsetY { get; set; }
        public int UiHeightFromBottom { get; set; }
        public int DotOffsetY { get; set; }
        public int DetectionBoxWidth { get; set; }
        public int DetectionBoxHeight { get; set; }
        public double DynamicFillRateSmall { get; set; }
        public double DynamicFillRateMedium { get; set; }
    }

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
        public Dictionary<string, ModeSpecificNmsSettings> ModeSpecificNms { get; set; }
        public ModeSpecificNmsSettings GlobalNms { get; set; } = new();
    }

    public class PlayerDetectionSettings
    {
        public int SmallBarWidthLimit { get; set; }
        public int MediumBarWidthLimit { get; set; }
        public bool EnableAsyncProcessing { get; set; }
        public int ProcessingTimeoutMs { get; set; }
        public double MinAspectRatio { get; set; }
        public double MaxAspectRatio { get; set; }
    }

    public class ModeSpecificNmsSettings
    {
        public double IouThreshold { get; set; }
        public double ConfidenceThreshold { get; set; }
        public int MaxResults { get; set; }
    }

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

    #endregion
}