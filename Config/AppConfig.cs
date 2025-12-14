using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using ArtaleAI.Models;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Map;
using ArtaleAI.Models.Minimap;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArtaleAI.Config
{
    /// <summary>
    /// 應用程式組態設定類別
    /// 使用 Singleton 模式管理全域設定，支援從 YAML 檔案載入和儲存
    /// </summary>
    public class AppConfig : INotifyPropertyChanged
    {
        private static AppConfig? _instance;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// 取得 AppConfig 單例實例（執行緒安全）
        /// </summary>
        /// <exception cref="InvalidOperationException">尚未初始化時拋出</exception>
        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException("AppConfig not initialized");
                }
                return _instance;
            }
        }

        #region 基礎設定
        
        /// <summary>遊戲視窗標題</summary>
        public string GameWindowTitle { get; set; } = "";
        
        /// <summary>上次選擇的視窗名稱</summary>
        public string LastSelectedWindowName { get; set; } = "";
        
        /// <summary>上次選擇的程序名稱</summary>
        public string LastSelectedProcessName { get; set; } = "";
        
        /// <summary>上次選擇的程序 ID</summary>
        public int LastSelectedProcessId { get; set; }
        
        /// <summary>小地圖放大倍數</summary>
        public int MinimapUpscaleFactor { get; set; }
        
        /// <summary>縮放係數</summary>
        public decimal ZoomFactor { get; set; }
        
        #endregion

        #region UI 設定
        
        /// <summary>放大鏡視窗大小（像素）</summary>
        public int MagnifierSize { get; set; }
        
        /// <summary>放大鏡偏移量（像素）</summary>
        public int MagnifierOffset { get; set; }
        
        /// <summary>十字準心大小（像素）</summary>
        public int CrosshairSize { get; set; }
        
        #endregion

        #region 檢測設定
        
        /// <summary>預設檢測閾值（0.0-1.0）</summary>
        public double DefaultThreshold { get; set; }
        
        /// <summary>最大檢測結果數量</summary>
        public int MaxDetectionResults { get; set; }
        
        /// <summary>檢測模式名稱</summary>
        public string DetectionMode { get; set; }
        
        /// <summary>NMS IoU 閾值（用於去重）</summary>
        public double NmsIouThreshold { get; set; }
        
        #endregion

        #region 模板設定
        
        /// <summary>玩家標記檢測閾值</summary>
        public double PlayerThreshold { get; set; }
        
        /// <summary>角點檢測閾值</summary>
        public double CornerThreshold { get; set; }
        
        /// <summary>玩家標記模板路徑</summary>
        public string PlayerMarker { get; set; }
        
        /// <summary>其他玩家標記模板路徑</summary>
        public string OtherPlayers { get; set; }
        
        #endregion

        #region 血條檢測設定
        
        /// <summary>最小血條寬度（像素）</summary>
        public int MinBarWidth { get; set; }
        
        /// <summary>最大血條寬度（像素）</summary>
        public int MaxBarWidth { get; set; }
        
        /// <summary>最小血條高度（像素）</summary>
        public int MinBarHeight { get; set; }
        
        /// <summary>最大血條高度（像素）</summary>
        public int MaxBarHeight { get; set; }
        
        /// <summary>最小填充率（0.0-1.0）</summary>
        public double MinFillRate { get; set; }
        
        /// <summary>紅色 HSV 下界 [H,S,V]</summary>
        public int[] LowerRedHsv { get; set; }
        
        /// <summary>紅色 HSV 上界 [H,S,V]</summary>
        public int[] UpperRedHsv { get; set; }
        
        /// <summary>小血條寬度上限（像素）</summary>
        public int SmallBarWidthLimit { get; set; }
        
        /// <summary>中血條寬度上限（像素）</summary>
        public int MediumBarWidthLimit { get; set; }
        
        /// <summary>最小長寬比</summary>
        public double MinAspectRatio { get; set; }
        
        /// <summary>最大長寬比</summary>
        public double MaxAspectRatio { get; set; }
        
        /// <summary>圓點垂直偏移量（像素）</summary>
        public int DotOffsetY { get; set; }
        
        /// <summary>檢測框寬度（像素）</summary>
        public int DetectionBoxWidth { get; set; }
        
        /// <summary>檢測框高度（像素）</summary>
        public int DetectionBoxHeight { get; set; }
        
        /// <summary>UI 距離底部的高度（像素）</summary>
        public int UiHeightFromBottom { get; set; }
        
        /// <summary>最小血條面積（像素²）</summary>
        public int MinBarArea { get; set; }
        
        /// <summary>動態小血條填充率</summary>
        public double DynamicFillRateSmall { get; set; }
        
        /// <summary>動態中血條填充率</summary>
        public double DynamicFillRateMedium { get; set; }
        
        /// <summary>玩家垂直偏移量（像素）</summary>
        public int PlayerOffsetY { get; set; }
        
        #endregion

        #region 檢測模式映射設定
        
        /// <summary>檢測模式配置字典（結構化配置）</summary>
        public Dictionary<string, DetectionModeConfig> DetectionModes { get; set; } = new();
        
        /// <summary>預設檢測模式</summary>
        public string DefaultMode { get; set; }
        
        /// <summary>顯示順序列表</summary>
        public List<string> DisplayOrder { get; set; } = new();
        
        /// <summary>
        /// 取得模式的顯示名稱（向後相容）
        /// </summary>
        public Dictionary<string, string> DisplayNames
        {
            get
            {
                var result = new Dictionary<string, string>();
                if (DetectionModes != null)
                {
                    foreach (var kvp in DetectionModes)
                    {
                        if (kvp.Value != null && !string.IsNullOrEmpty(kvp.Value.DisplayName))
                        {
                            result[kvp.Key] = kvp.Value.DisplayName;
                        }
                    }
                }
                return result;
            }
        }
        
        /// <summary>
        /// 取得遮擋處理映射表（向後相容）
        /// </summary>
        public Dictionary<string, string> OcclusionMappings
        {
            get
            {
                var result = new Dictionary<string, string>();
                if (DetectionModes != null)
                {
                    foreach (var kvp in DetectionModes)
                    {
                        if (kvp.Value != null && !string.IsNullOrEmpty(kvp.Value.Occlusion))
                        {
                            result[kvp.Key] = kvp.Value.Occlusion;
                        }
                    }
                }
                return result;
            }
        }
        
        #endregion

        #region 檢測間隔設定
        
        /// <summary>血條檢測間隔（毫秒）</summary>
        public int BloodBarDetectIntervalMs { get; set; }
        
        /// <summary>怪物檢測間隔（毫秒）</summary>
        public int MonsterDetectIntervalMs { get; set; }
        
        /// <summary>畫面擷取幀率（FPS）</summary>
        public int CaptureFrameRate { get; set; }
        
        #endregion

        #region 地圖編輯設定
        
        /// <summary>刪除半徑（像素）</summary>
        public double DeletionRadius { get; set; }
        
        /// <summary>路徑點圓圈半徑（像素）</summary>
        public int WaypointCircleRadius { get; set; }
        
        /// <summary>路徑點顏色</summary>
        public string WaypointColor { get; set; }
        
        /// <summary>安全區顏色</summary>
        public string SafeZoneColor { get; set; }
        
        #endregion

        #region 路徑規劃設定
        
        /// <summary>連續檢測間隔（毫秒）</summary>
        public int ContinuousDetectionIntervalMs { get; set; }
        
        /// <summary>玩家位置檢測閾值</summary>
        public double PlayerPositionThreshold { get; set; }
        
        /// <summary>其他玩家檢測閾值</summary>
        public double OtherPlayersThreshold { get; set; }
        
        /// <summary>是否啟用其他玩家檢測</summary>
        public bool EnableOtherPlayersDetection { get; set; }
        
        /// <summary>路徑點到達判定距離（像素）</summary>
        public double WaypointReachDistance { get; set; }
        
        /// <summary>最大追蹤歷史記錄數量</summary>
        public int MaxTrackingHistory { get; set; }
        
        /// <summary>是否啟用自動路徑尋找</summary>
        public bool EnableAutoPathFinding { get; set; }
        
        /// <summary>是否啟用自動角色移動控制</summary>
        public bool EnableAutoMovement { get; set; }
        
        /// <summary>平台邊界處理設定</summary>
        public PlatformBoundsConfig PlatformBounds { get; set; } = new();
        
        #endregion

        #region 攻擊範圍設定
        
        /// <summary>攻擊範圍寬度（像素）</summary>
        public int AttackRangeWidth { get; set; }
        
        /// <summary>攻擊範圍高度（像素）</summary>
        public int AttackRangeHeight { get; set; }
        
        /// <summary>攻擊範圍水平偏移（像素）</summary>
        public int AttackRangeOffsetX { get; set; }
        
        /// <summary>攻擊範圍垂直偏移（像素）</summary>
        public int AttackRangeOffsetY { get; set; }
        
        #endregion

        #region 角點模板設定
        
        /// <summary>左上角模板路徑</summary>
        public string TopLeft { get; set; }
        
        /// <summary>右上角模板路徑</summary>
        public string TopRight { get; set; }
        
        /// <summary>左下角模板路徑</summary>
        public string BottomLeft { get; set; }
        
        /// <summary>右下角模板路徑</summary>
        public string BottomRight { get; set; }
        
        #endregion

        #region 視覺化樣式設定
        
        /// <summary>怪物檢測框樣式</summary>
        public MonsterStyle Monster { get; set; } = new();
        
        /// <summary>血條樣式</summary>
        public PartyRedBarStyle PartyRedBar { get; set; } = new();
        
        /// <summary>檢測框樣式</summary>
        public DetectionBoxStyle DetectionBox { get; set; } = new();
        
        /// <summary>攻擊範圍樣式</summary>
        public AttackRangeStyle AttackRange { get; set; } = new();
        
        /// <summary>小地圖樣式</summary>
        public MinimapStyle Minimap { get; set; } = new();
        
        /// <summary>小地圖玩家標記樣式</summary>
        public MinimapPlayerStyle MinimapPlayer { get; set; } = new();
        
        #endregion

        #region 初始化與儲存方法

        /// <summary>
        /// 初始化 AppConfig 單例
        /// 從 YAML 設定檔載入設定，若檔案不存在則使用預設值
        /// </summary>
        /// <param name="configPath">設定檔路徑（預設為 config.yaml）</param>
        public static void Initialize(string configPath = "config.yaml")
        {
            // 執行緒安全的單例初始化
            lock (_lock)
            {
                if (_instance != null)
                {
                    return; // 已經初始化，避免重複初始化
                }

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
        }

        /// <summary>
        /// 儲存當前設定到 YAML 檔案
        /// 使用 YamlDotNet 序列化設定並寫入檔案
        /// </summary>
        /// <param name="configPath">設定檔路徑（預設為 config.yaml）</param>
        /// <exception cref="InvalidOperationException">儲存失敗時拋出</exception>
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

        /// <summary>
        /// 從 JSON 檔案載入地圖資料
        /// 檔案不存在或載入失敗時返回空的 MapData
        /// </summary>
        /// <param name="filePath">地圖檔案完整路徑</param>
        /// <returns>載入的地圖資料物件</returns>
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

        /// <summary>
        /// 將地圖資料儲存到 JSON 檔案
        /// 使用縮排格式便於閱讀和編輯
        /// </summary>
        /// <param name="mapData">要儲存的地圖資料</param>
        /// <param name="filePath">目標檔案完整路徑</param>
        /// <exception cref="InvalidOperationException">儲存失敗時拋出</exception>
        public void SaveMapToFile(MapData mapData, string filePath)
        {
            try
            {
                // 使用自訂轉換器強制顯示小數點，確保精度可見
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Converters = { new FloatArrayConverter() }
                };
                var json = JsonSerializer.Serialize(mapData, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save map: {ex.Message}", ex);
            }
        }

        #endregion

        #region INotifyPropertyChanged 實作

        /// <summary>
        /// 屬性變更通知事件
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 觸發屬性變更通知
        /// </summary>
        /// <param name="propertyName">變更的屬性名稱</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// 平台邊界處理設定
    /// 控制邊界檢測的緩衝區大小和冷卻時間
    /// </summary>
    public class PlatformBoundsConfig
    {
        /// <summary>緩衝區大小（像素，接近邊界時提前觸發減速）</summary>
        public double BufferZone { get; set; } = 5.0;
        
        /// <summary>緊急區域（像素，超出此範圍強制停止）</summary>
        public double EmergencyZone { get; set; } = 2.0;
        
        /// <summary>邊界事件冷卻時間（毫秒，防止反覆觸發）</summary>
        public int CooldownMs { get; set; } = 500;
    }
}