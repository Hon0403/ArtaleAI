using ArtaleAI.Config;
using System.Text.Json;
using Windows.Graphics.Capture;

namespace ArtaleAI.Models
{
    #region 檢測相關枚舉

    public enum MonsterDetectionMode
    {
        Basic, ContourOnly, Grayscale, Color, TemplateFree
    }

    public enum OcclusionHandling
    {
        None, MorphologyRepair, DynamicThreshold, MultiScale
    }

    #endregion

    #region 地圖編輯相關枚舉和介面

    /// <summary>
    /// 小地圖的使用情境模式
    /// </summary>
    public enum MinimapUsage
    {
        /// <summary>路徑編輯模式 - 靜態小地圖用於編輯</summary>
        PathEditing,
        /// <summary>即時顯示模式 - 動態疊加層用於即時偵測</summary>
        LiveViewOverlay
    }

    ///
    /// 定義了所有編輯模式的種類。
    ///
    public enum EditMode
    {
        None,
        Waypoint, // ● 路線標記
        SafeZone, // 🟩 安全區域
        RestrictedZone, // 🟥 禁止區域
        Rope, // 🧗 繩索路徑
        Delete // ❌ 刪除標記
    }

    ///
    /// 所有地圖標記物件的基礎介面。
    ///
    public interface IMapObject
    {
        public Guid Id { get; }
    }

    #endregion

    #region 渲染相關介面和類

    ///
    /// 渲染項目的基礎介面
    ///
    public interface IRenderItem
    {
        Rectangle BoundingBox { get; }
        string DisplayText { get; }
        Color FrameColor { get; }
        Color TextColor { get; }
        int FrameThickness { get; }
        double TextScale { get; }
        int TextThickness { get; }
    }

    ///
    /// 統一的顏色解析工具類
    ///
    public static class ColorHelper
    {
        public static Color ParseColor(string colorString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(colorString))
                    return Color.Yellow;

                var parts = colorString.Split(',');
                if (parts.Length >= 3)
                {
                    int r = int.Parse(parts[0].Trim());
                    int g = int.Parse(parts[1].Trim());
                    int b = int.Parse(parts[2].Trim());
                    if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                        return Color.FromArgb(r, g, b);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"顏色解析失敗: {colorString} - {ex.Message}");
            }
            return Color.Yellow;
        }
    }

    ///
    /// 怪物渲染項目
    ///
    public class MonsterRenderItem : IRenderItem
    {
        public Rectangle BoundingBox { get; set; }
        public string MonsterName { get; set; } = "";
        public double Confidence { get; set; }
        private readonly MonsterOverlayStyle _style;

        public MonsterRenderItem(MonsterOverlayStyle style)
        {
            _style = style ?? throw new ArgumentNullException(nameof(style));
        }

        public string DisplayText => _style.ShowConfidence
            ? string.Format(_style.TextFormat, MonsterName, Confidence)
            : MonsterName;
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    ///
    /// 隊友血條渲染項目
    ///
    public class PartyRedBarRenderItem : IRenderItem
    {
        public Rectangle BoundingBox { get; set; }
        private readonly PartyRedBarOverlayStyle _style;

        public PartyRedBarRenderItem(PartyRedBarOverlayStyle style)
        {
            _style = style ?? throw new ArgumentNullException(nameof(style));
        }

        public string DisplayText => _style.RedBarDisplayName;
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    ///
    /// 檢測框渲染項目
    ///
    public class DetectionBoxRenderItem : IRenderItem
    {
        public Rectangle BoundingBox { get; set; }
        private readonly DetectionBoxOverlayStyle _style;

        public DetectionBoxRenderItem(DetectionBoxOverlayStyle style)
        {
            _style = style ?? throw new ArgumentNullException(nameof(style));
        }

        public string DisplayText => _style.BoxDisplayName;
        public Color FrameColor => ColorHelper.ParseColor(_style.FrameColor);
        public Color TextColor => ColorHelper.ParseColor(_style.TextColor);
        public int FrameThickness => _style.FrameThickness;
        public double TextScale => _style.TextScale;
        public int TextThickness => _style.TextThickness;
    }

    #endregion

    #region 渲染相關模型

    public class MonsterRenderInfo
    {
        public Point Location { get; set; }
        public Size Size { get; set; }
        public string MonsterName { get; set; } = "";
        public double Confidence { get; set; }
    }

    #endregion

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
        public string SelectedMonsterName { get; set; } = "";
        public List<Bitmap> Templates { get; set; } = new();
        public string DetectionMode { get; set; } = "";
        public double Threshold { get; set; } = 0.7;
        public int TemplateCount { get; set; } = 0;
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

    ///
    /// 代表一條由多個 Waypoint 組成的連續路徑。
    ///
    public class MapPath : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public List<Waypoint> Points { get; set; } = new List<Waypoint>();
    }

    ///
    /// 代表一個路徑點。
    ///
    public class Waypoint : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public PointF Position { get; set; }
    }

    ///
    /// 代表一個多邊形區域（例如可行走、不可進入）。
    ///
    public class MapArea : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public List<PointF> Points { get; set; } = new List<PointF>();
    }

    ///
    /// 代表一條繩索或梯子路徑。
    ///
    public class Rope : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public PointF Start { get; set; }
        public PointF End { get; set; }
    }

    ///
    /// 負責管理一張地圖上的所有標記數據，並處理檔案的儲存與讀取。
    ///
    public class MapData
    {
        public List<MapPath> WaypointPaths { get; set; } = new List<MapPath>();
        public List<MapArea> SafeZone { get; set; } = new List<MapArea>();
        public List<Rope> Ropes { get; set; } = new List<Rope>();
        // 用來儲存單點的「禁止區域」標記
        public List<Waypoint> RestrictedPoints { get; set; } = new List<Waypoint>();

        ///
        /// 將目前的地圖數據儲存到指定的 JSON 檔案。
        ///
        public void SaveToFile(string filePath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, jsonString);
        }

        ///
        /// 從指定的 JSON 檔案載入地圖數據。
        ///
        public static MapData? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string jsonString = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<MapData>(jsonString);
        }
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
