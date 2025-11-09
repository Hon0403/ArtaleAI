using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Config
{
    /// <summary>
    /// 簡化版 AppConfig - 移除所有包裝層
    /// </summary>
    public class AppConfig : INotifyPropertyChanged
    {
        private static AppConfig? _instance;
        public static AppConfig Instance => _instance ?? throw new InvalidOperationException("AppConfig not initialized");

        // 基礎設定 - 直接屬性，無包裝層
        public string GameWindowTitle { get; set; } = "";
        public string LastSelectedWindowName { get; set; } = "";
        public string LastSelectedProcessName { get; set; } = "";
        public int LastSelectedProcessId { get; set; }
        public int MinimapUpscaleFactor { get; set; }
        public decimal ZoomFactor { get; set; }

        // UI設定 - 直接屬性
        public int MagnifierSize { get; set; }
        public int MagnifierOffset { get; set; }
        public int CrosshairSize { get; set; }

        // 檢測設定 - 直接屬性
        public double DefaultThreshold { get; set; }
        public int MaxDetectionResults { get; set; }
        public string DetectionMode { get; set; }
        public double NmsIouThreshold { get; set; }

        // 模板設定 - 直接屬性
        public double PlayerThreshold { get; set; }
        public double CornerThreshold { get; set; }
        public string PlayerMarker { get; set; }
        public string OtherPlayers { get; set; }

        // 血條檢測設定
        public int MinBarWidth { get; set; }
        public int MaxBarWidth { get; set; }
        public int MinBarHeight { get; set; }
        public int MaxBarHeight { get; set; }
        public double MinFillRate { get; set; }
        public int[] LowerRedHsv { get; set; }
        public int[] UpperRedHsv { get; set; }
        public int SmallBarWidthLimit { get; set; }
        public int MediumBarWidthLimit { get; set; }
        public double MinAspectRatio { get; set; }
        public double MaxAspectRatio { get; set; }
        public int DotOffsetY { get; set; }
        public int DetectionBoxWidth { get; set; }
        public int DetectionBoxHeight { get; set; }
        public int UiHeightFromBottom { get; set; }
        public int MinBarArea { get; set; }
        public double DynamicFillRateSmall { get; set; }
        public double DynamicFillRateMedium { get; set; }
        public int PlayerOffsetY { get; set; }

        public Dictionary<string, string> ModeMapping { get; set; }
        public Dictionary<string, string> DisplayNames { get; set; }
        public Dictionary<string, string> OcclusionMappings { get; set; }
        public Dictionary<string, string> ModeDescriptions { get; set; } = new();
        public Dictionary<string, int> PerformanceLevels { get; set; }
        public string DefaultMode { get; set; }
        public List<string> DisplayOrder { get; set; }

        // 檢測間隔設定
        public int BloodBarDetectIntervalMs { get; set; }
        public int MonsterDetectIntervalMs { get; set; }
        public int CaptureFrameRate { get; set; }

        // 地圖編輯設定
        public double DeletionRadius { get; set; }
        public int WaypointCircleRadius { get; set; }
        public string WaypointColor { get; set; }
        public string SafeZoneColor { get; set; }

        // 路徑規劃設定
        public int ContinuousDetectionIntervalMs { get; set; }
        public double PlayerPositionThreshold { get; set; }
        public double OtherPlayersThreshold { get; set; }
        public bool EnableOtherPlayersDetection { get; set; }
        public double WaypointReachDistance { get; set; }
        public int MaxTrackingHistory { get; set; }
        public bool EnableAutoPathFinding { get; set; }

        // 攻擊範圍設定
        public int AttackRangeWidth { get; set; }
        public int AttackRangeHeight { get; set; }
        public int AttackRangeOffsetX { get; set; }
        public int AttackRangeOffsetY { get; set; }

        // 角點模板
        public string TopLeft { get; set; }
        public string TopRight { get; set; }
        public string BottomLeft { get; set; }
        public string BottomRight { get; set; }

        // 辨識框設定
        public MonsterStyle Monster { get; set; } = new();
        public PartyRedBarStyle PartyRedBar { get; set; } = new();
        public DetectionBoxStyle DetectionBox { get; set; } = new();
        public AttackRangeStyle AttackRange { get; set; } = new();
        public MinimapStyle Minimap { get; set; } = new();
        public MinimapPlayerStyle MinimapPlayer { get; set; } = new();

        #region 簡化的初始化方法

        public static void Initialize(string configPath = "config.yaml")
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var yaml = File.ReadAllText(configPath);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();
                    _instance = deserializer.Deserialize<AppConfig>(yaml);
                }
                else
                {
                    _instance = new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config 載入失敗，使用預設值: {ex.Message}");
                _instance = new AppConfig();
            }
        }

        public void Save(string configPath = "config.yaml")
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yaml = serializer.Serialize(this);
                File.WriteAllText(configPath, yaml);
                OnPropertyChanged(nameof(AppConfig));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save config: {ex.Message}", ex);
            }
        }

        public MapData LoadMapFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new MapData();

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<MapData>(json);
            }
            catch
            {
                return new MapData();
            }
        }

        public void SaveMapToFile(MapData mapData, string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(mapData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save map: {ex.Message}", ex);
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}