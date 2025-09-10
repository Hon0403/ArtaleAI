using ArtaleAI.API;
using ArtaleAI.Config;
using ArtaleAI.Detection;
using ArtaleAI.Display;
using ArtaleAI.GameWindow;
using ArtaleAI.Minimap;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdRect = System.Drawing.Rectangle;
using SdSize = System.Drawing.Size;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {

        #region Private Fields - 完整版
        private ConfigManager? _configurationManager;

        public ConfigManager? ConfigurationManager => _configurationManager;
        private MapDetector? _mapDetector;
        private GraphicsCaptureItem? _selectedCaptureItem;
        private MapEditor? _mapEditor;
        private List<Bitmap> _currentMonsterTemplates = new();
        private string? _currentMonsterName;

        private int _consecutiveSkippedFrames = 0;
        // 檢測狀態管理
        private Rectangle? _currentMinimapRect;
        private List<Rectangle> _currentBloodBars = new();
        private List<Rectangle> _currentDetectionBoxes = new();
        private List<Rectangle> _currentAttackRangeBoxes = new();
        private List<MonsterRenderInfo> _currentMonsters = new();
        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;

        private GraphicsCapturer? _capturer;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;
        private bool _isLiveViewRunning = false;

        // 圖像同步鎖
        private Bitmap? _currentDisplayFrame;

        // 其他服務
        private FloatingMagnifier? _floatingMagnifier;
        private MapFileManager? _mapFileManager;
        private MonsterImageFetcher? _monsterDownloader;
        #endregion

        #region Constructor & Initialization

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            BindEvents();

        }

        private void InitializeServices()
        {
            _configurationManager = new ConfigManager(this);
            _configurationManager.Load();

            var mapEditorSettings = _configurationManager?.CurrentConfig?.MapEditor;
            var trajectorySettings = _configurationManager?.CurrentConfig?.Trajectory;
            _mapEditor = new MapEditor(mapEditorSettings, trajectorySettings);

            // 初始化檢測服務
            var detectionSettings = _configurationManager?.CurrentConfig?.Templates?.MonsterDetection;
            var templateMatchingSettings = _configurationManager?.CurrentConfig?.TemplateMatching;

            TemplateMatcher.Initialize(detectionSettings, templateMatchingSettings, _configurationManager?.CurrentConfig);
            _mapDetector = new MapDetector(_configurationManager?.CurrentConfig ?? new AppConfig());

            // 其他服務
            var uiSettings = _configurationManager?.CurrentConfig?.Ui;
            _floatingMagnifier = new FloatingMagnifier(this, uiSettings);
            _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
            _mapFileManager.InitializeMapFilesDropdown();
            _monsterDownloader = new MonsterImageFetcher(this);

            InitializeMonsterTemplateSystem();

            InitializeDetectionModeDropdown();
        }

        private void InitializeMonsterTemplateSystem()
        {
            var monsterNames = MonsterTemplateStore.GetAvailableMonsterNames(GetMonstersDirectory());
            cbo_MonsterTemplates.Items.Clear();
            foreach (var name in monsterNames)
                cbo_MonsterTemplates.Items.Add(name);

            cbo_MonsterTemplates.SelectedIndexChanged += OnMonsterSelectionChanged;
            OnStatusMessage($"成功載入 {monsterNames.Count} 種怪物模板選項");
        }

        // 即時顯示事件
        public void OnFrameAvailable(Bitmap frame)
        {
            if (frame == null) return;

            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (config?.DetectionPerformance == null) return;

                // 🎯 單執行緒：循序處理
                ProcessBloodBars(frame);
                ProcessMonsters(frame);
                RenderAndDisplayOverlays(frame);
            }
            catch (Exception ex)
            {
                OnStatusMessage($"幀處理失敗: {ex.Message}");
            }
            finally
            {
                frame?.Dispose();
            }
        }


        private void ProcessBloodBars(Bitmap frame)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (!ShouldDetectBloodBar(DateTime.UtcNow, config.DetectionPerformance)) return;

                using var frameMat = UtilityHelper.BitmapToThreeChannelMat(frame);
                using var cameraArea = BloodBarDetector.ExtractCameraArea(frameMat, null, config.PartyRedBar, out int cameraOffsetY);
                using var hsvImage = UtilityHelper.ConvertToHSV(cameraArea);
                using var redMask = BloodBarDetector.CreateRedMask(hsvImage, config.PartyRedBar);

                var bloodBarRect = BloodBarDetector.FindBestRedBar(redMask, config.PartyRedBar);
                if (bloodBarRect.HasValue)
                {
                    var screenBloodBar = BloodBarDetector.ToScreenCoordinates(bloodBarRect.Value, cameraOffsetY);
                    _currentBloodBars = new List<Rectangle> { screenBloodBar };
                    _currentDetectionBoxes = BloodBarDetector.CalculateDetectionBoxes(screenBloodBar, config.PartyRedBar);
                    _currentAttackRangeBoxes = BloodBarDetector.CalculateAttackRangeBoxes(screenBloodBar, config.AttackRange);
                    _lastBloodBarDetection = DateTime.UtcNow;
                    OnStatusMessage($"✅ 血條檢測成功: {screenBloodBar}");
                }
            }
            catch (Exception ex) { OnStatusMessage($"❌ 血條檢測異常: {ex.Message}"); }
        }

        private void ProcessMonsters(Bitmap frame)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (config?.DetectionPerformance == null) return;

                var now = DateTime.UtcNow;

                OnStatusMessage($"🔍 怪物檢測開始: 檢測區域={_currentDetectionBoxes.Count}個");

                // 🎯 單執行緒：直接使用當前檢測框，不需要複製
                if (_currentDetectionBoxes.Any() && ShouldDetectMonster(now, config.DetectionPerformance))
                {
                    OnStatusMessage($"🎯 開始怪物檢測，使用 {_currentDetectionBoxes.Count} 個檢測框");

                    var monsters = DetectMonstersInCurrentBoxes(frame, _currentDetectionBoxes);

                    // 🎯 單執行緒：直接更新，不需要鎖定
                    _currentMonsters = monsters;

                    _lastMonsterDetection = now;
                    if (monsters.Any())
                    {
                        OnStatusMessage($"🎯 發現 {monsters.Count} 個怪物");
                    }
                    else
                    {
                        OnStatusMessage($"⚠️ 未發現怪物，檢測框={_currentDetectionBoxes.Count}個");
                    }
                }
                else if (!_currentDetectionBoxes.Any())
                {
                    OnStatusMessage("❌ 無檢測區域，等待血條檢測成功");
                }
                else
                {
                    OnStatusMessage($"⏰ 怪物檢測間隔未到，距離上次={(now - _lastMonsterDetection).TotalMilliseconds:F0}ms");
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage($"❌ 怪物檢測異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 同步版本的怪物檢測方法 - 用於各別處理
        /// </summary>
        private List<MonsterRenderInfo> DetectMonstersInCurrentBoxes(Bitmap frame, List<Rectangle> detectionBoxes)
        {
            try
            {
                var templateData = GetTemplateDataSafely();
                if (string.IsNullOrEmpty(templateData.SelectedMonsterName) || !templateData.Templates.Any())
                    return new List<MonsterRenderInfo>();

                var allResults = new List<MonsterRenderInfo>();

                foreach (var originalDetectionBox in detectionBoxes)
                {
                    try
                    {
                        // ✅ 1. 計算實際可裁切的區域
                        var frameBounds = new Rectangle(0, 0, frame.Width, frame.Height);
                        var validCropArea = Rectangle.Intersect(originalDetectionBox, frameBounds);

                        if (validCropArea.IsEmpty || validCropArea.Width < 10 || validCropArea.Height < 10)
                        {
                            Console.WriteLine($"⚠️ 跳過無效檢測區域: {validCropArea}");
                            continue;
                        }

                        // ✅ 2. 裁切圖像
                        using var croppedFrame = frame.Clone(validCropArea, frame.PixelFormat);
                        if (croppedFrame == null) continue;

                        Console.WriteLine($"🔍 檢測區域: 原始={originalDetectionBox}, 實際裁切={validCropArea}");

                        // ✅ 3. 在裁切圖像中進行檢測
                        var croppedResults = ProcessMonstersInCroppedFrame(
                            croppedFrame,
                            templateData,
                            validCropArea,           // 實際裁切區域
                            originalDetectionBox     // 原始檢測框（用於調試）
                        );

                        allResults.AddRange(croppedResults);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ 檢測框處理失敗: {ex.Message}");
                        continue;
                    }
                }

                // ✅ 4. 應用NMS去除重複
                if (allResults.Count > 1)
                {
                    double iouThreshold = _configurationManager.CurrentConfig.Templates.MonsterDetection.NmsIouThreshold;
                    Console.WriteLine($"🎯 應用NMS，閾值={iouThreshold}, 候選數={allResults.Count}");
                    allResults = UtilityHelper.ApplyNMS(allResults, iouThreshold, higherIsBetter: true);
                    Console.WriteLine($"✅ NMS後剩餘: {allResults.Count} 個結果");
                }

                return allResults;
            }
            catch (Exception ex)
            {
                OnStatusMessage($"❌ 怪物檢測失敗: {ex.Message}");
                return new List<MonsterRenderInfo>();
            }
        }

        /// <summary>
        /// 處理裁切後的圖像檢測
        /// </summary>
        private List<MonsterRenderInfo> ProcessMonstersInCroppedFrame(
            Bitmap croppedFrame,
            TemplateData templateData,
            Rectangle actualCropArea,
            Rectangle originalDetectionBox)
        {
            var results = new List<MonsterRenderInfo>();

            try
            {
                // 增強調試輸出
                OnStatusMessage($"🔍 開始裁切圖像檢測:");
                OnStatusMessage($"  - 裁切圖像: {croppedFrame.Width}x{croppedFrame.Height}");
                OnStatusMessage($"  - 模板數量: {templateData.Templates.Count}");
                OnStatusMessage($"  - 怪物名稱: '{templateData.SelectedMonsterName}'");
                OnStatusMessage($"  - 檢測模式: {templateData.DetectionMode}");
                OnStatusMessage($"  - 閾值: {templateData.Threshold:F3}");

                // 檢查模板是否為空
                if (templateData.Templates.Count == 0)
                {
                    OnStatusMessage("❌ 沒有可用模板!");
                    return results;
                }

                // 檢查第一個模板
                var firstTemplate = templateData.Templates[0];
                if (firstTemplate != null)
                {
                    OnStatusMessage($"📏 第一個模板尺寸: {firstTemplate.Width}x{firstTemplate.Height}");

                    // 檢查尺寸是否合理
                    if (firstTemplate.Width > croppedFrame.Width || firstTemplate.Height > croppedFrame.Height)
                    {
                        OnStatusMessage($"❌ 模板太大! 模板:{firstTemplate.Width}x{firstTemplate.Height}, 檢測區域:{croppedFrame.Width}x{croppedFrame.Height}");
                        return results;
                    }
                }

                // 解析檢測模式
                if (!Enum.TryParse<MonsterDetectionMode>(templateData.DetectionMode, out var detectionMode))
                {
                    detectionMode = MonsterDetectionMode.Color;
                    OnStatusMessage("⚠️ 檢測模式解析失敗，使用預設 Color 模式");
                }

                OnStatusMessage("🎯 開始模板匹配...");

                // 執行模板匹配並記錄每一步
                var matchResults = TemplateMatcher.FindMonstersWithCache(
                    croppedFrame,
                    templateData.Templates,
                    detectionMode,
                    templateData.Threshold,
                    templateData.SelectedMonsterName,
                    null
                );

                OnStatusMessage($"📊 模板匹配完成，原始結果: {matchResults.Count} 個");

                // ✅ 處理每個結果並記錄詳細資訊
                foreach (var match in matchResults)
                {
                    var globalX = match.Position.X + actualCropArea.X;
                    var globalY = match.Position.Y + actualCropArea.Y;

                    OnStatusMessage($"👾 候選怪物:");
                    OnStatusMessage($"  - 名稱: {match.Name}");
                    OnStatusMessage($"  - 裁切內座標: ({match.Position.X}, {match.Position.Y})");
                    OnStatusMessage($"  - 全局座標: ({globalX}, {globalY})");
                    OnStatusMessage($"  - 信心度: {match.Confidence:F4}");
                    OnStatusMessage($"  - 尺寸: {match.Size.Width}x{match.Size.Height}");

                    // ✅ 極低閾值測試
                    if (match.Confidence >= 0.01) // 極低閾值
                    {
                        var monster = new MonsterRenderInfo
                        {
                            MonsterName = match.Name,
                            Location = new SdPoint(globalX, globalY),
                            Size = match.Size,
                            Confidence = match.Confidence
                        };
                        results.Add(monster);
                        OnStatusMessage($"  ✅ 已添加怪物 (信心度: {match.Confidence:F4})");
                    }
                    else
                    {
                        OnStatusMessage($"  ❌ 信心度過低: {match.Confidence:F4}");
                    }
                }

                OnStatusMessage($"✅ 最終結果: {results.Count} 個怪物");
                return results;
            }
            catch (Exception ex)
            {
                OnStatusMessage($"❌ 裁切圖像檢測異常: {ex.Message}");
                OnStatusMessage($"❌ 堆疊追蹤: {ex.StackTrace}");
                return results;
            }
        }

        // 血條檢測條件判斷
        private bool ShouldDetectBloodBar(DateTime now, DetectionPerformanceSettings config)
        {
            var elapsed = (now - _lastBloodBarDetection).TotalMilliseconds;
            return elapsed >= config.BloodBarDetectIntervalMs || _currentBloodBars.Count == 0;
        }

        // 怪物檢測條件判斷
        private bool ShouldDetectMonster(DateTime now, DetectionPerformanceSettings config)
        {
            var elapsed = (now - _lastMonsterDetection).TotalMilliseconds;
            return elapsed >= config.MonsterDetectIntervalMs || _currentMonsters.Count == 0;
        }

        private void RenderAndDisplayOverlays(Bitmap baseBitmap)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (config?.OverlayStyle == null)
                {
                    OnStatusMessage("⚠️ 渲染樣式配置無效，顯示原始畫面");
                    UpdateDisplaySafely(new Bitmap(baseBitmap));
                    return;
                }

                var monsterItems = new List<MonsterRenderItem>();
                var partyRedBarItems = new List<PartyRedBarRenderItem>();
                var detectionBoxItems = new List<DetectionBoxRenderItem>();
                var attackRangeItems = new List<AttackRangeRenderItem>();

                if (_currentBloodBars.Any())
                {
                    partyRedBarItems.AddRange(_currentBloodBars.Select(rect =>
                        new PartyRedBarRenderItem(config.OverlayStyle.PartyRedBar) { BoundingBox = rect }));
                }

                if (_currentDetectionBoxes.Any())
                {
                    detectionBoxItems.AddRange(_currentDetectionBoxes.Select(rect =>
                        new DetectionBoxRenderItem(config.OverlayStyle.DetectionBox) { BoundingBox = rect }));
                }

                // 攻擊範圍框渲染
                if (_currentAttackRangeBoxes.Any())
                {
                    attackRangeItems.AddRange(_currentAttackRangeBoxes.Select(rect =>
                        new AttackRangeRenderItem(config.OverlayStyle.AttackRange) { BoundingBox = rect }));
                }

                if (_currentMonsters.Any())
                {
                    monsterItems.AddRange(_currentMonsters.Select(m =>
                        new MonsterRenderItem(config.OverlayStyle.Monster)
                        {
                            BoundingBox = new Rectangle(m.Location.X, m.Location.Y, m.Size.Width, m.Size.Height),
                            MonsterName = m.MonsterName,
                            Confidence = m.Confidence
                        }));
                }

                // 修正：合併所有檢測框項目為 IRenderItem 列表
                var allDetectionItems = new List<IRenderItem>();
                allDetectionItems.AddRange(detectionBoxItems.Cast<IRenderItem>());
                allDetectionItems.AddRange(attackRangeItems.Cast<IRenderItem>());

                var renderedFrame = SimpleRenderer.RenderOverlays(
                    baseBitmap,
                    monsterItems,
                    null,
                    null,
                    partyRedBarItems,
                    allDetectionItems 
                );

                if (renderedFrame != null)
                {
                    UpdateDisplaySafely(renderedFrame);
                }
            }
            catch (Exception ex)
            {
                OnError($"渲染疊加層失敗: {ex.Message}");
            }
        }

        private void UpdateDisplaySafely(Bitmap newFrame)
        {
            if (newFrame == null) return;

            if (pictureBoxLiveView.InvokeRequired)
            {
                pictureBoxLiveView.BeginInvoke(() => {
                    UpdateFrameInternal(newFrame);
                });
            }
            else
            {
                UpdateFrameInternal(newFrame);
            }
        }

        private void UpdateFrameInternal(Bitmap newFrame)
        {
            if (pictureBoxLiveView.IsDisposed)
            {
                newFrame?.Dispose();
                return;
            }

            var oldImage = pictureBoxLiveView.Image;
            var oldFrame = _currentDisplayFrame;

            _currentDisplayFrame = newFrame;
            pictureBoxLiveView.Image = newFrame;

            // 只釋放舊的資源
            if (oldImage != newFrame)
            {
                oldImage?.Dispose();
            }

            if (oldFrame != newFrame)
            {
                oldFrame?.Dispose();
            }
        }

        private void BindEvents()
        {
            // UI 控制項事件綁定
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged;

            // 地圖編輯模式事件
            rdo_PathMarker.CheckedChanged += OnEditModeChanged;
            rdo_SafeZone.CheckedChanged += OnEditModeChanged;
            rdo_RestrictedZone.CheckedChanged += OnEditModeChanged;
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            // 小地圖滑鼠事件
            pictureBoxMinimap.Paint += pictureBoxMinimap_Paint;
            pictureBoxMinimap.MouseDown += pictureBoxMinimap_MouseDown;
            pictureBoxMinimap.MouseUp += pictureBoxMinimap_MouseUp;
            pictureBoxMinimap.MouseMove += pictureBoxMinimap_MouseMove;
            pictureBoxMinimap.MouseLeave += pictureBoxMinimap_MouseLeave;

            // 按鈕事件
            btn_SaveMap.Click += btn_SaveMap_Click;
            btn_New.Click += btn_New_Click;

        }

        #endregion

        #region IConfigEventHandler 實作

        public void OnConfigLoaded(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigLoaded), config);
                return;
            }

            numericUpDownZoom.Value = config.General.ZoomFactor;
            OnStatusMessage("配置檔案載入完成");
        }

        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigSaved), config);
                return;
            }

            OnStatusMessage("設定已儲存");
        }

        public void OnConfigError(string errorMessage)
        {
            OnError($"設定錯誤: {errorMessage}");
        }

        #endregion

        #region 辨識模式控制

        /// <summary>
        /// 初始化辨識模式下拉選單
        /// </summary>
        private void InitializeDetectionModeDropdown()
        {
            cbo_DetectMode.Items.Clear();

            var config = _configurationManager.CurrentConfig;
            var detectionModes = config.DetectionModes;

            if (detectionModes?.DisplayOrder != null && detectionModes.DisplayNames != null)
            {
                try
                {
                    // 按設定檔順序添加項目
                    foreach (var mode in detectionModes.DisplayOrder)
                    {
                        if (detectionModes.DisplayNames.TryGetValue(mode, out var displayName))
                        {
                            cbo_DetectMode.Items.Add(displayName);
                        }
                    }

                    // 設置預設選擇
                    var defaultMode = detectionModes.DefaultMode;
                    if (detectionModes.DisplayNames.TryGetValue(defaultMode, out var defaultDisplay))
                    {
                        cbo_DetectMode.SelectedItem = defaultDisplay;
                    }

                    OnStatusMessage($"智慧辨識模式初始化完成，預設：{defaultMode}");
                }
                catch (Exception ex)
                {
                    OnError($"辨識模式設定檔格式錯誤: {ex.Message}");
                }
            }
            else
            {
                OnError("辨識模式設定檔缺少必要配置，使用預設模式");
            }

            // 綁定事件
            cbo_DetectMode.SelectedIndexChanged += OnDetectionModeChanged;
        }

        /// <summary>
        /// 辨識模式變更事件 - 重構版
        /// </summary>
        private void OnDetectionModeChanged(object? sender, EventArgs e)
        {
            var selectedDisplayText = cbo_DetectMode.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedDisplayText))
            {
                var selectedMode = ExtractModeFromDisplayText(selectedDisplayText);
                var optimalOcclusion = GetOptimalOcclusionForMode(selectedMode);

                _configurationManager?.SetValue(cfg =>
                {
                    if (cfg.Templates?.MonsterDetection != null)
                    {
                        cfg.Templates.MonsterDetection.DetectionMode = selectedMode;
                        cfg.Templates.MonsterDetection.OcclusionHandling = optimalOcclusion.ToString();
                    }
                }, autoSave: true);

                OnStatusMessage($"辨識模式已切換至: {selectedMode} (自動使用 {optimalOcclusion} 遮擋處理)");
            }
        }

        /// <summary>
        /// 從顯示文字提取模式
        /// </summary>
        private string ExtractModeFromDisplayText(string displayText)
        {
            var config = _configurationManager.CurrentConfig;
            var detectionModes = config.DetectionModes;

            //  優先使用設定檔映射
            if (detectionModes?.DisplayNames != null)
            {
                var mode = detectionModes.DisplayNames.FirstOrDefault(kvp => kvp.Value == displayText).Key;
                if (!string.IsNullOrEmpty(mode))
                {
                    return mode;
                }
            }

            OnError($"辨識模式設定檔讀取失敗或格式錯誤：'{displayText}'");
            return config.DetectionModes.DefaultMode;
        }

        /// <summary>
        /// 獲取模式的最佳遮擋處理 - 基於設定檔
        /// </summary>
        private OcclusionHandling GetOptimalOcclusionForMode(string mode)
        {
            var config = _configurationManager?.CurrentConfig;
            var occlusionMappings = config?.DetectionModes?.OcclusionMappings;

            if (occlusionMappings?.TryGetValue(mode, out var occlusionString) == true)
            {
                return Enum.TryParse<OcclusionHandling>(occlusionString, out var result)
                    ? result
                    : OcclusionHandling.None;
            }

            OnError($"找不到模式 '{mode}' 的遮擋處理設定");
            return OcclusionHandling.None;
        }

        #endregion

        #region IMapFileEventHandler 實作

        public string GetMapDataDirectory() => UtilityHelper.GetMapDataDirectory();

        public void OnMapLoaded(string mapFileName)
        {
            OnStatusMessage($"成功載入地圖: {mapFileName}");
        }

        public void OnMapSaved(string mapFileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, bool>(OnMapSaved), mapFileName, isNewFile);
                return;
            }

            string message = isNewFile ? "新地圖儲存成功！" : "儲存成功！";
            MessageBox.Show(message, "地圖檔案管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OnStatusMessage($"地圖儲存: {mapFileName}");
        }

        public void OnNewMapCreated()
        {
            OnStatusMessage("已建立新地圖");
        }

        public void UpdateWindowTitle(string title)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(UpdateWindowTitle), title);
                return;
            }

            this.Text = title;
        }

        public void RefreshMinimap()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(RefreshMinimap));
                return;
            }

            pictureBoxMinimap.Invalidate();
        }

        #endregion

        #region IApplicationEventHandler 實作

        // 放大鏡功能
        public Bitmap? GetSourceImage() => pictureBoxMinimap.Image as Bitmap;

        public decimal GetZoomFactor() =>
            _configurationManager.CurrentConfig.General.ZoomFactor;

        public PointF? ConvertToImageCoordinates(SdPoint mouseLocation)
        {
            if (pictureBoxMinimap.Image == null) return null;
            var clientSize = pictureBoxMinimap.ClientSize;
            var imageSize = pictureBoxMinimap.Image.Size;
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY);
            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;
            var displayRect = new Rectangle(offsetX, offsetY, displayWidth, displayHeight);
            if (!displayRect.Contains(mouseLocation)) return null;
            float imageX = mouseLocation.X - offsetX;
            float imageY = mouseLocation.Y - offsetY;
            float originalX = imageX / ratio;
            float originalY = imageY / ratio;
            return new PointF(originalX, originalY);
        }

        // 怪物模板功能
        public string GetMonstersDirectory() => UtilityHelper.GetMonstersDirectory();

        public void OnTemplatesLoaded(string monsterName, int templateCount)
        {
            OnStatusMessage($"成功載入 {templateCount} 個 '{monsterName}' 的模板");
        }

        #endregion

        #region 統一狀態訊息處理

        public void OnStatusMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnStatusMessage), message);
                return;
            }

            textBox1.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\r\n");
            textBox1.ScrollToCaret();
        }

        public void OnError(string errorMessage)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnError), errorMessage);
                return;
            }

            textBox1.AppendText($"{DateTime.Now:HH:mm:ss} - ❌ {errorMessage}\r\n");
            textBox1.ScrollToCaret();
            MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion

        #region UI 事件處理

        private void numericUpDownZoom_ValueChanged(object? sender, EventArgs e)
        {
            _configurationManager?.SetValue(cfg =>
            {
                if (cfg.General != null)
                    cfg.General.ZoomFactor = numericUpDownZoom.Value;
            }, autoSave: true);
        }

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            //  完全停止並釋放所有分頁資源
            await StopAndReleaseAllResources();

            //  根據當前分頁啟動對應功能
            switch (tabControl1.SelectedIndex)
            {
                case 1: // 路徑編輯
                    await StartPathEditingModeAsync();
                    break;
                case 2: // 即時顯示
                    await StartLiveViewModeAsync();
                    break;
            }
        }

        /// <summary>
        /// 路徑編輯模式：只載入靜態小地圖
        /// </summary>
        private async Task StartPathEditingModeAsync()
        {
            OnStatusMessage("🗺️ 路徑編輯模式：載入靜態小地圖");

            tabControl1.Enabled = false;
            try
            {
                // 載入一次性的小地圖快照
                await LoadMinimapAsync(MinimapUsage.PathEditing);
                OnStatusMessage(" 路徑編輯模式就緒");
            }
            catch (Exception ex)
            {
                OnError($"路徑編輯模式啟動失敗: {ex.Message}");
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }

        /// <summary>
        /// 即時顯示模式：啟動所有即時處理功能
        /// </summary>
        private async Task StartLiveViewModeAsync()
        {
            OnStatusMessage("📺 即時顯示模式：啟動");
            var config = _configurationManager?.CurrentConfig ?? new AppConfig();

            try
            {
                await StartLiveViewAsync(config);
                OnStatusMessage("✅ 即時顯示模式就緒");
            }
            catch (Exception ex)
            {
                OnError($"即時顯示模式啟動失敗: {ex.Message}");
            }
        }
        public bool IsLiveViewRunning => _isLiveViewRunning;

        public async Task StartLiveViewAsync(AppConfig config)
        {
            if (_isLiveViewRunning)
            {
                OnStatusMessage("即時顯示已經在運行中");
                return;
            }

            try
            {
                OnStatusMessage("正在尋找遊戲視窗...");

                // 尋找遊戲視窗
                var captureItem = WindowFinder.TryCreateItemForWindow(config.General.GameWindowTitle);
                if (captureItem == null)
                {
                    OnError($"找不到名為 '{config.General.GameWindowTitle}' 的遊戲視窗");
                    return;
                }

                OnStatusMessage("✅ 成功找到遊戲視窗");

                // 建立捕捉器
                _capturer = new GraphicsCapturer(captureItem);
                _cancellationTokenSource = new CancellationTokenSource();
                OnStatusMessage("🎥 即時顯示已啟動");
                _isLiveViewRunning = true;

                // 開始捕捉任務
                _captureTask = CaptureLoopAsync(_cancellationTokenSource.Token);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                OnError($"啟動即時顯示失敗: {ex.Message}");
                await StopLiveViewAsync();
            }
        }

        public async Task StopLiveViewAsync()
        {
            if (!_isLiveViewRunning) return;

            try
            {
                _isLiveViewRunning = false;
                _cancellationTokenSource?.Cancel();

                if (_captureTask != null && !_captureTask.IsCompleted)
                {
                    await _captureTask;
                }

                OnStatusMessage("🛑 即時顯示已停止");
            }
            catch (TaskCanceledException)
            {
                // 正常的取消操作，忽略
            }
            catch (Exception ex)
            {
                OnError($"停止即時顯示時發生錯誤: {ex.Message}");
            }
            finally
            {
                _capturer?.Dispose();
                _capturer = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _captureTask = null;
            }
        }

        private async Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                int targetFPS = config.WindowCapture.CaptureFrameRate;
                int captureDelayMs = 1000 / targetFPS;

                OnStatusMessage($"🎥 捕捉幀率設定為 {targetFPS} FPS (間隔 {captureDelayMs}ms)");

                await Task.Yield();

                while (!cancellationToken.IsCancellationRequested && _capturer != null)
                {
                    using var frame = _capturer.TryGetNextFrame();
                    if (frame != null)
                    {
                        // 🎯 單執行緒：直接處理，不需要節流控制
                        try
                        {
                            var safeCopy = new Bitmap(frame.Width, frame.Height, frame.PixelFormat);
                            using (var g = Graphics.FromImage(safeCopy))
                            {
                                g.DrawImage(frame, 0, 0);
                            }

                            // 🎯 關鍵：直接在主執行緒處理，不使用Task.Run
                            OnFrameAvailable(safeCopy);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"處理幀失敗: {ex.Message}");
                        }
                    }

                    await Task.Delay(captureDelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError($"捕捉過程發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 完全停止並釋放所有分頁處理資源
        /// </summary>
        private async Task StopAndReleaseAllResources()
        {
            OnStatusMessage("🛑 停止所有處理...");

            if (IsLiveViewRunning)
            {
                await StopLiveViewAsync();
                OnStatusMessage("✅ 即時顯示服務已停止");
            }

            // 清理狀態
            _currentMonsters.Clear();
            _currentBloodBars.Clear();
            _currentDetectionBoxes.Clear();
            _currentAttackRangeBoxes.Clear();
            GC.Collect();
            OnStatusMessage("✅ 資源已清理");
        }

        /// <summary>
        /// 統一的小地圖載入方法 - 重構版
        /// </summary>
        private async Task<MinimapLoadResult?> LoadMinimapAsync(MinimapUsage usage)
        {
            var config = _configurationManager?.CurrentConfig ?? new AppConfig();
            Action<string> reporter = message => OnStatusMessage(message);

            OnStatusMessage($"正在載入小地圖快照 ({usage})...");

            var result = await _mapDetector?.GetSnapshotAsync(this.Handle, config, _selectedCaptureItem, reporter);

            if (result?.MinimapImage != null)
            {
                switch (usage)
                {
                    case MinimapUsage.PathEditing:
                        pictureBoxMinimap.Image?.Dispose();
                        pictureBoxMinimap.Image = result.MinimapImage;
                        _selectedCaptureItem = result.CaptureItem;
                        OnStatusMessage("✅ 路徑編輯小地圖載入完成");
                        break;
                    case MinimapUsage.LiveViewOverlay:
                        SetupLiveViewOverlay(result);
                        OnStatusMessage("✅ 即時顯示小地圖疊加層設置完成");
                        break;
                }
                return new MinimapLoadResult(result.MinimapImage, result.CaptureItem);
            }

            OnStatusMessage("❌ 小地圖載入失敗或已取消");
            return null;
        }

        /// <summary>
        /// 設置即時顯示的小地圖疊加層
        /// </summary>
        private void SetupLiveViewOverlay(MinimapSnapshotResult result)
        {
            if (result?.MinimapImage == null) return;

            try
            {
                // 檢查動態辨識是否成功
                if (!result.MinimapScreenRect.HasValue)
                {
                    OnError("動態小地圖位置辨識失敗，無法設置疊加層");
                    return;
                }

                // 使用動態偵測到的玩家位置
                Rectangle playerRect;
                if (result.PlayerPosition.HasValue)
                {
                    var pos = result.PlayerPosition.Value;
                    playerRect = new Rectangle(pos.X - 8, pos.Y - 8, 16, 16);
                }
                else
                {
                    // 如果沒有玩家位置，使用空矩形
                    playerRect = Rectangle.Empty;
                    OnStatusMessage("⚠️ 未檢測到玩家位置");
                }

                //  直接使用動態偵測到的小地圖螢幕位置
                Rectangle minimapOnScreen = result.MinimapScreenRect.Value;


                OnStatusMessage($" 小地圖疊加層已設置 ({minimapOnScreen.Width}x{minimapOnScreen.Height})");
            }
            catch (Exception ex)
            {
                OnError($"設置小地圖疊加層失敗: {ex.Message}");
            }
        }

        #endregion

        #region 地圖編輯事件

        private void OnEditModeChanged(object? sender, EventArgs e)
        {
            if (sender is not RadioButton checkedButton || !checkedButton.Checked)
                return;

            EditMode selectedMode = checkedButton.Name switch
            {
                nameof(rdo_PathMarker) => EditMode.Waypoint,
                nameof(rdo_SafeZone) => EditMode.SafeZone,
                nameof(rdo_RestrictedZone) => EditMode.RestrictedZone,
                nameof(rdo_RopeMarker) => EditMode.Rope,
                nameof(rdo_DeleteMarker) => EditMode.Delete,
                _ => EditMode.None
            };

            _mapEditor.SetEditMode(selectedMode);
            pictureBoxMinimap.Invalidate();

            OnStatusMessage($"編輯模式切換至: {selectedMode}");

        }

        #endregion

        #region PictureBox 滑鼠事件

        private void pictureBoxMinimap_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var imgPoint = ConvertToImageCoordinates(e.Location);
                if (imgPoint.HasValue)
                {
                    _mapEditor?.StartDrawing(imgPoint.Value);
                    pictureBoxMinimap.Invalidate();
                }
            }
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            // 放大鏡功能
            _floatingMagnifier?.UpdateMagnifier(e.Location, pictureBoxMinimap);

            var imgPoint = ConvertToImageCoordinates(e.Location);
            if (imgPoint.HasValue)
            {
                _mapEditor?.UpdatePreview(imgPoint.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var imgPoint = ConvertToImageCoordinates(e.Location);
                if (imgPoint.HasValue)
                {
                    _mapEditor?.FinishDrawing(imgPoint.Value);
                    pictureBoxMinimap.Invalidate();
                }
            }
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            _mapEditor?.Render(e.Graphics, pointF => ConvertToDisplayCoordinates(SdPoint.Round(pointF)));
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            _floatingMagnifier?.Hide();

        }

        #endregion

        #region 按鈕事件

        private void btn_SaveMap_Click(object sender, EventArgs e)
        {
            try
            {
                _mapFileManager?.SaveCurrentMap();
            }
            catch (Exception ex)
            {
                OnError($"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        private void btn_New_Click(object sender, EventArgs e)
        {
            try
            {
                _mapFileManager?.CreateNewMap();
            }
            catch (Exception ex)
            {
                OnError($"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        #endregion


        #region 清理與釋放


        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                _currentDisplayFrame?.Dispose();
                _currentDisplayFrame = null;

                ClearCurrentMonsterTemplates();
                UtilityHelper.ClearMonsterTemplateCache();

                // 清理其他資源
                _floatingMagnifier?.Dispose();
                _monsterDownloader?.Dispose();
                _mapDetector?.Dispose();
                pictureBoxMinimap.Image?.Dispose();
                pictureBoxLiveView.Image?.Dispose();

                OnStatusMessage("應用程式已清理完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理資源時發生錯誤: {ex.Message}");
            }

            base.OnFormClosed(e);
        }

        #endregion

        /// <summary>
        /// 安全獲取模板資料的方法 - UI執行緒安全
        /// </summary>
        private TemplateData GetTemplateDataSafely()
        {
            if (InvokeRequired)
            {
                return (TemplateData)Invoke(() => GetTemplateDataSafely());
            }

            return new TemplateData
            {
                SelectedMonsterName = cbo_MonsterTemplates.SelectedItem.ToString(),
                Templates = GetCurrentMonsterTemplates(),
                DetectionMode = ExtractModeFromDisplayText(cbo_DetectMode.SelectedItem.ToString()),
                Threshold = _configurationManager.CurrentConfig.Templates.MonsterDetection.DefaultThreshold,
                TemplateCount = GetMonsterTemplateCount()
            };
        }

        public SdPoint ConvertToDisplayCoordinates(SdPoint imagePoint)
        {
            if (pictureBoxMinimap.Image == null) return SdPoint.Empty;

            var clientSize = pictureBoxMinimap.ClientSize;
            var imageSize = pictureBoxMinimap.Image.Size;
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY);

            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;

            int controlX = (int)(imagePoint.X * ratio) + offsetX;
            int controlY = (int)(imagePoint.Y * ratio) + offsetY;

            return new SdPoint(controlX, controlY);
        }

        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (cbo_MonsterTemplates.SelectedItem == null) return;
            string selectedMonster = cbo_MonsterTemplates.SelectedItem.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(selectedMonster))
            {
                OnStatusMessage($"🔄 切換怪物模板：{selectedMonster}");
                ClearCurrentMonsterTemplates();

                _currentMonsterTemplates = await MonsterTemplateStore.LoadMonsterTemplatesAsync(
                    selectedMonster, GetMonstersDirectory(), OnStatusMessage);
                _currentMonsterName = selectedMonster;
                OnTemplatesLoaded(selectedMonster, _currentMonsterTemplates.Count);
            }
        }

        private void ClearCurrentMonsterTemplates()
        {
            foreach (var template in _currentMonsterTemplates)
            {
                template?.Dispose();
            }
            _currentMonsterTemplates.Clear();
            _currentMonsterName = null;
        }

        public List<Bitmap> GetCurrentMonsterTemplates()
        {
            return _currentMonsterTemplates.ToList(); // 返回副本
        }

        public int GetMonsterTemplateCount()
        {
            return _currentMonsterTemplates.Count;
        }

        private async void btn_DownloadMonster_Click(object sender, EventArgs e)
        {
            try
            {
                string monsterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "請輸入怪物名稱:", "下載怪物模板", "");
                if (string.IsNullOrWhiteSpace(monsterName))
                    return;

                btn_DownloadMonster.Enabled = false;
                btn_DownloadMonster.Text = "下載中...";
                var result = await _monsterDownloader.DownloadMonsterAsync(monsterName);
                if (result?.Success == true)
                {
                    var monsterNames = MonsterTemplateStore.GetAvailableMonsterNames(GetMonstersDirectory());
                    cbo_MonsterTemplates.Items.Clear();
                    foreach (var name in monsterNames)
                        cbo_MonsterTemplates.Items.Add(name);

                    OnStatusMessage($"下載完成！處理了 {result.DownloadedCount} 個檔案");
                }
            }
            catch (Exception ex)
            {
                OnError($"下載怪物時發生錯誤: {ex.Message}");
            }
            finally
            {
                btn_DownloadMonster.Enabled = true;
                btn_DownloadMonster.Text = "下載怪物";
            }
        }
    }
}

