using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Core.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Utils;
using OpenCvSharp;
using System.Diagnostics;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 檢測服務 - 處理怪物檢測、血條檢測、小地圖玩家追蹤
    /// </summary>
    public class DetectionService
    {
        #region Private Fields

        private readonly GameVisionCore _gameVision;
        private readonly TextBox _logTextBox;
        private readonly ComboBox _monsterTemplatesComboBox;
        private readonly ComboBox _detectModeComboBox;
        
        // 檢測結果 - 由外部傳入引用
        private List<Rectangle> _currentDetectionBoxes;
        private List<Rectangle> _currentMinimapBoxes;
        private List<Rectangle> _currentMinimapMarkers;
        private List<DetectionResult> _currentMonsters;

        // 怪物模板
        private List<Mat> _currentMonsterMatTemplates = new();
        private string _selectedMonsterName = string.Empty;
        
        // 檢測時間戳
        private DateTime _lastMonsterDetection = DateTime.MinValue;

        #endregion

        #region Constructor

        public DetectionService(
            GameVisionCore gameVision,
            TextBox logTextBox,
            ComboBox monsterTemplatesComboBox,
            ComboBox detectModeComboBox,
            List<Rectangle> currentDetectionBoxes,
            List<Rectangle> currentMinimapBoxes,
            List<Rectangle> currentMinimapMarkers,
            List<DetectionResult> currentMonsters)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
            _logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            _monsterTemplatesComboBox = monsterTemplatesComboBox ?? throw new ArgumentNullException(nameof(monsterTemplatesComboBox));
            _detectModeComboBox = detectModeComboBox ?? throw new ArgumentNullException(nameof(detectModeComboBox));
            
            _currentDetectionBoxes = currentDetectionBoxes ?? throw new ArgumentNullException(nameof(currentDetectionBoxes));
            _currentMinimapBoxes = currentMinimapBoxes ?? throw new ArgumentNullException(nameof(currentMinimapBoxes));
            _currentMinimapMarkers = currentMinimapMarkers ?? throw new ArgumentNullException(nameof(currentMinimapMarkers));
            _currentMonsters = currentMonsters ?? throw new ArgumentNullException(nameof(currentMonsters));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 初始化怪物模板系統
        /// </summary>
        public void InitializeMonsterTemplateSystem()
        {
            try
            {
                var monstersDirectory = PathManager.MonstersDirectory;
                var monsterNames = new List<string>();

                if (Directory.Exists(monstersDirectory))
                {
                    var subDirectories = Directory.GetDirectories(monstersDirectory);
                    monsterNames = subDirectories.Select(Path.GetFileName).ToList();
                }

                _monsterTemplatesComboBox.Items.Clear();
                foreach (var name in monsterNames)
                {
                    _monsterTemplatesComboBox.Items.Add(name);
                }

                _monsterTemplatesComboBox.SelectedIndexChanged += OnMonsterSelectionChanged;
                MsgLog.ShowStatus(_logTextBox, $"✅ 載入 {monsterNames.Count} 個怪物模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_logTextBox, $"初始化怪物模板系統失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化辨識模式下拉選單
        /// </summary>
        public void InitializeDetectionModeDropdown()
        {
            _detectModeComboBox.Items.Clear();
            var config = AppConfig.Instance;

            if (config.Vision.DisplayOrder?.Any() == true && config.Vision.DetectionModes?.Any() == true)
            {
                try
                {
                    foreach (var mode in config.Vision.DisplayOrder)
                    {
                        if (config.Vision.DetectionModes.TryGetValue(mode, out var modeConfig))
                        {
                            _detectModeComboBox.Items.Add(modeConfig.DisplayName);
                        }
                    }

                    var defaultMode = config.Vision.DefaultMode;
                    if (config.Vision.DetectionModes.TryGetValue(defaultMode, out var defaultModeConfig))
                    {
                        _detectModeComboBox.SelectedItem = defaultModeConfig.DisplayName;
                    }

                    MsgLog.ShowStatus(_logTextBox, $"檢測模式已載入：{config.Vision.DisplayOrder.Count} 個模式，預設：{defaultMode}");
                }
                catch (Exception ex)
                {
                    MsgLog.ShowError(_logTextBox, $"檢測模式初始化失敗: {ex.Message}");
                }
            }
            else
            {
                MsgLog.ShowError(_logTextBox, "❌ 檢測模式配置無效");
            }

            _detectModeComboBox.SelectedIndexChanged += OnDetectionModeChanged;
        }

        /// <summary>
        /// 處理怪物檢測
        /// </summary>
        public void ProcessMonsters(Mat frameMat)
        {
            var config = AppConfig.Instance;
            var now = DateTime.UtcNow;

            // 1. 時間間隔檢查
            var elapsed = (now - _lastMonsterDetection).TotalMilliseconds;
            if (elapsed < config.Vision.MonsterDetectIntervalMs && _currentMonsters.Count > 0)
                return;

            // 2. 前置條件檢查
            if (!_currentDetectionBoxes.Any())
            {
                MsgLog.ShowStatus(_logTextBox, "無血條檢測範圍");
                return;
            }

            if (string.IsNullOrEmpty(_selectedMonsterName) || !_currentMonsterMatTemplates.Any())
            {
                MsgLog.ShowStatus(_logTextBox, $"未選擇怪物模板 (Templates={_currentMonsterMatTemplates.Count})");
                return;
            }

            try
            {
                var allResults = new List<DetectionResult>();
                var frameBounds = new Rect(0, 0, frameMat.Width, frameMat.Height);

                // 3. 取得檢測模式
                var detectionModeString = config.Vision.DetectionMode ?? "Color";
                if (!Enum.TryParse<MonsterDetectionMode>(detectionModeString, out var detectionMode))
                    detectionMode = MonsterDetectionMode.Color;

                // 4. 逐個檢測框處理
                foreach (var detectionBox in _currentDetectionBoxes)
                {
                    var cropRect = new Rect(detectionBox.X, detectionBox.Y, detectionBox.Width, detectionBox.Height);
                    var validCropRect = frameBounds.Intersect(cropRect);

                    if (validCropRect.Width < 10 || validCropRect.Height < 10)
                        continue;

                    using var croppedMat = frameMat[validCropRect].Clone();

                    // 5. 怪物偵測
                    var results = _gameVision.FindMonsters(
                        croppedMat,
                        _currentMonsterMatTemplates,
                        detectionMode,
                        config.Vision.PlayerThreshold,
                        _selectedMonsterName
                    ) ?? new List<DetectionResult>();

                    // 6. 座標轉換
                    foreach (var result in results)
                    {
                        var monster = new DetectionResult(
                            result.Name,
                            new System.Drawing.Point(result.Position.X + validCropRect.X, result.Position.Y + validCropRect.Y),
                            result.Size,
                            result.Confidence,
                            new Rectangle(result.Position.X + validCropRect.X, result.Position.Y + validCropRect.Y,
                                         result.Size.Width, result.Size.Height)
                        );
                        allResults.Add(monster);
                    }
                }

                // 7. NMS去重
                if (allResults.Count > 1)
                {
                    var dedupedResults = GameVisionCore.ApplyNMS(allResults, iouThreshold: 0.3, higherIsBetter: true);
                    _currentMonsters.Clear();
                    _currentMonsters.AddRange(dedupedResults);
                    MsgLog.ShowStatus(_logTextBox, $"檢測到 {allResults.Count} 個怪物 (NMS後: {dedupedResults.Count})");
                }
                else
                {
                    _currentMonsters.Clear();
                    _currentMonsters.AddRange(allResults);
                }

                _lastMonsterDetection = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                MsgLog.ShowStatus(_logTextBox, $"怪物檢測錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理資源
        /// </summary>
        public void Dispose()
        {
            foreach (var template in _currentMonsterMatTemplates)
            {
                template?.Dispose();
            }
            _currentMonsterMatTemplates.Clear();
        }

        #endregion

        #region Private Event Handlers

        /// <summary>
        /// 怪物模板選擇變更事件
        /// </summary>
        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            try
            {
                if (_monsterTemplatesComboBox.SelectedItem == null) return;

                string selectedMonster = _monsterTemplatesComboBox.SelectedItem.ToString();
                if (string.IsNullOrEmpty(selectedMonster)) return;

                MsgLog.ShowStatus(_logTextBox, $"載入怪物模板: {selectedMonster}");

                // 清理現有模板
                foreach (var template in _currentMonsterMatTemplates)
                {
                    template?.Dispose();
                }
                _currentMonsterMatTemplates.Clear();

                // 載入新模板
                _currentMonsterMatTemplates = await _gameVision.LoadMonsterTemplatesAsync(
                    selectedMonster,
                    PathManager.MonstersDirectory
                ) ?? new List<Mat>();

                _selectedMonsterName = selectedMonster;
                MsgLog.ShowStatus(_logTextBox, $"✅ 已載入 {_currentMonsterMatTemplates.Count} 個模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_logTextBox, $"載入模板錯誤: {ex.Message}");

                foreach (var template in _currentMonsterMatTemplates)
                {
                    template?.Dispose();
                }
                _currentMonsterMatTemplates.Clear();
            }
        }

        /// <summary>
        /// 辨識模式變更事件
        /// </summary>
        private void OnDetectionModeChanged(object? sender, EventArgs e)
        {
            var selectedDisplayText = _detectModeComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisplayText)) return;

            var config = AppConfig.Instance;

            // 1. 從顯示名稱找到模式 key
            var selectedMode = config.Vision.DetectionModes?
                .FirstOrDefault(kvp => kvp.Value.DisplayName == selectedDisplayText).Key
                ?? config.Vision.DefaultMode ?? "Color";

            // 2. 找到最佳遮擋設定
            var optimalOcclusion = OcclusionHandling.None;
            if (config.Vision.DetectionModes?.TryGetValue(selectedMode, out var occlusionString) == true)
            {
                if (Enum.TryParse<OcclusionHandling>(occlusionString.Occlusion, out var result))
                    optimalOcclusion = result;
            }

            // 3. 套用設定
            AppConfig.Instance.Vision.DetectionMode = selectedMode;

            MsgLog.ShowStatus(_logTextBox, $"✅ 偵測模式: {selectedMode} | 遮擋: {optimalOcclusion}");
        }

        #endregion
    }
}
