using ArtaleAI.Config;
using ArtaleAI.Interfaces;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 統一的怪物服務 - 整合模板管理、偵測功能和玩家血條檢測 (OpenCvSharp 版本)
    /// </summary>
    public class MonsterService : IDisposable
    {
        private readonly IMainFormEvents _eventHandler;
        private readonly ComboBox _monsterComboBox;
        private readonly AppConfig _config;
        private readonly PartyRedBarSettings _redBarSettings;
        private readonly PlayerDetectionSettings? _playerSettings;

        private List<Bitmap> _currentTemplates;
        private bool _isProcessing = false;
        private readonly object _processingLock = new();

        public List<Bitmap> CurrentTemplates => _currentTemplates.AsReadOnly().ToList();
        public bool HasTemplates => _currentTemplates.Any();
        public string? CurrentMonsterName { get; private set; }

        public MonsterService(ComboBox monsterComboBox, IMainFormEvents eventHandler)
        {
            _monsterComboBox = monsterComboBox ?? throw new ArgumentNullException(nameof(monsterComboBox));
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _config = eventHandler.ConfigurationManager?.CurrentConfig ?? new AppConfig();
            _redBarSettings = _config.PartyRedBar ?? new PartyRedBarSettings();
            _playerSettings = _config.PlayerDetection;

            _currentTemplates = new List<Bitmap>();
            _monsterComboBox.SelectedIndexChanged += OnMonsterSelectionChanged;
        }

        #region 怪物模板管理

        /// <summary>
        /// 初始化怪物模板下拉選單
        /// </summary>
        public void InitializeMonsterDropdown()
        {
            try
            {
                _monsterComboBox.Items.Clear();
                string monstersDirectory = _eventHandler.GetMonstersDirectory();

                if (!Directory.Exists(monstersDirectory))
                {
                    _eventHandler.OnStatusMessage($"怪物模板目錄不存在: {monstersDirectory}");
                    return;
                }

                var monsterFolders = Directory.GetDirectories(monstersDirectory);
                if (!monsterFolders.Any())
                {
                    _eventHandler.OnStatusMessage("未找到任何怪物模板資料夾");
                    return;
                }

                foreach (var folder in monsterFolders)
                {
                    string monsterName = new DirectoryInfo(folder).Name;
                    _monsterComboBox.Items.Add(monsterName);
                }

                _eventHandler.OnStatusMessage($"成功載入 {monsterFolders.Length} 種怪物模板選項");
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"初始化怪物下拉選單失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 載入指定怪物的模板
        /// </summary>
        public async Task LoadMonsterTemplates(string monsterName)
        {
            try
            {
                ClearCurrentTemplates();

                string monsterFolderPath = Path.Combine(_eventHandler.GetMonstersDirectory(), monsterName);
                if (!Directory.Exists(monsterFolderPath))
                {
                    _eventHandler.OnError($"找不到怪物資料夾: {monsterFolderPath}");
                    return;
                }

                _eventHandler.OnStatusMessage($"正在從 '{monsterName}' 載入怪物模板...");

                var templateFiles = await Task.Run(() => Directory.GetFiles(monsterFolderPath, "*.png"));
                if (!templateFiles.Any())
                {
                    _eventHandler.OnStatusMessage($"在 '{monsterName}' 資料夾中未找到任何PNG模板檔案");
                    return;
                }

                var templateTasks = templateFiles.Select(async file =>
                {
                    try
                    {
                        return await Task.Run(() =>
                        {
                            using (var tempBitmap = new Bitmap(file))
                            {
                                return new Bitmap(tempBitmap);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _eventHandler.OnStatusMessage($"載入模板檔案失敗: {Path.GetFileName(file)} - {ex.Message}");
                        return null;
                    }
                });

                var loadedTemplates = await Task.WhenAll(templateTasks);
                _currentTemplates.AddRange(loadedTemplates.Where(t => t != null));

                CurrentMonsterName = monsterName;
                _eventHandler.OnTemplatesLoaded(monsterName, _currentTemplates.Count);
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"載入怪物模板時發生錯誤: {ex.Message}");
            }
        }

        private void ClearCurrentTemplates()
        {
            foreach (var template in _currentTemplates)
            {
                template?.Dispose();
            }
            _currentTemplates.Clear();
            CurrentMonsterName = null;
        }

        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (_monsterComboBox.SelectedItem == null) return;

            string selectedMonster = _monsterComboBox.SelectedItem.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedMonster))
            {
                await LoadMonsterTemplates(selectedMonster);
            }
        }

        #endregion

        #region 玩家血條檢測 (原 PlayerDetector.cs 功能)

        /// <summary>
        /// 通過隊友紅色血條獲取玩家位置 - 修復版本
        /// </summary>
        public (System.Drawing.Point? playerLocation, System.Drawing.Point? redBarLocation, Rectangle? redBarRect)
            GetPlayerLocationByPartyRedBar(Bitmap frameBitmap, Rectangle? minimapRect = null, Rectangle? uiExcludeRect = null)
        {
            if (frameBitmap == null) return (null, null, null);

            try
            {
                using var frameMat = UtilityHelper.BitmapToThreeChannelMat(frameBitmap);

                // 1. 清零小地圖區域避免干擾
                if (minimapRect.HasValue)
                {
                    var minimapRegion = new Rect(minimapRect.Value.X, minimapRect.Value.Y,
                        minimapRect.Value.Width, minimapRect.Value.Height);
                    frameMat[minimapRegion].SetTo(new Scalar(0, 0, 0));
                }

                // 記錄相機區域的偏移量
                int cameraOffsetY = 0;

                // 2. 提取相機區域（排除UI）
                using var cameraArea = ExtractCameraArea(frameMat, uiExcludeRect, out cameraOffsetY);
                if (cameraArea.Empty()) return (null, null, null);

                using var hsvImage = UtilityHelper.ConvertToHSV(cameraArea);
                var lowerRed = ToOpenCvHsv((_redBarSettings.LowerRedHsv[0], _redBarSettings.LowerRedHsv[1], _redBarSettings.LowerRedHsv[2]));
                var upperRed = ToOpenCvHsv((_redBarSettings.UpperRedHsv[0], _redBarSettings.UpperRedHsv[1], _redBarSettings.UpperRedHsv[2]));

                using var redMask = new Mat();
                Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);

                var redBarResult = FindPartyRedBarWithSize(redMask);
                if (!redBarResult.HasValue) return (null, null, null);

                var (redBarLocation, redBarRect) = redBarResult.Value;

                // 關鍵修正：將相機區域座標轉換為全螢幕座標
                var fullScreenRedBarLocation = new System.Drawing.Point(
                    redBarLocation.X,
                    redBarLocation.Y + cameraOffsetY // 加上相機區域的Y偏移
                );

                var fullScreenRedBarRect = new Rectangle(
                    redBarRect.X,
                    redBarRect.Y + cameraOffsetY, // 加上相機區域的Y偏移
                    redBarRect.Width,
                    redBarRect.Height
                );

                var playerLocation = new System.Drawing.Point(
                    fullScreenRedBarLocation.X + _redBarSettings.PlayerOffsetX,
                    fullScreenRedBarLocation.Y + _redBarSettings.PlayerOffsetY);

                return (playerLocation, fullScreenRedBarLocation, fullScreenRedBarRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 血條定位失敗: {ex.Message}");
                return (null, null, null);
            }
        }

        /// <summary>
        /// 使用設定檔的動態填充率參數
        /// </summary>
        private (System.Drawing.Point location, Rectangle rect)? FindPartyRedBarWithSize(Mat redMask)
        {
            var contours = new Mat[0];
            var hierarchy = new Mat();
            Cv2.FindContours(redMask, out contours, hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var candidates = new List<(System.Drawing.Point location, Rectangle rect, int area)>();
            try
            {
                foreach (var contour in contours)
                {
                    var boundingRect = Cv2.BoundingRect(contour);
                    var area = (int)Cv2.ContourArea(contour);
                    var fillRate = (double)area / (boundingRect.Width * boundingRect.Height);

                    int smallWidthLimit = _playerSettings?.SmallBarWidthLimit ?? 10;
                    int mediumWidthLimit = _playerSettings?.MediumBarWidthLimit ?? 25;

                    double minFillRateThreshold;
                    if (boundingRect.Width <= smallWidthLimit)
                        minFillRateThreshold = _redBarSettings.DynamicFillRateSmall;
                    else if (boundingRect.Width <= mediumWidthLimit)
                        minFillRateThreshold = _redBarSettings.DynamicFillRateMedium;
                    else
                        minFillRateThreshold = _redBarSettings.MinFillRate;

                    if (boundingRect.Height >= _redBarSettings.MinBarHeight &&
                        boundingRect.Height <= _redBarSettings.MaxBarHeight &&
                        boundingRect.Width >= _redBarSettings.MinBarWidth &&
                        boundingRect.Width <= _redBarSettings.MaxBarWidth &&
                        area >= _redBarSettings.MinBarArea &&
                        fillRate >= minFillRateThreshold)
                    {
                        var realRect = new Rectangle(
                            boundingRect.X, boundingRect.Y,
                            boundingRect.Width, boundingRect.Height);

                        candidates.Add((
                            new System.Drawing.Point(boundingRect.X, boundingRect.Y),
                            realRect,
                            area));
                    }
                }

                if (candidates.Any())
                {
                    var bestCandidate = candidates.OrderByDescending(c => c.area).First();
                    return (bestCandidate.location, bestCandidate.rect);
                }
            }
            finally
            {
                UtilityHelper.SafeDispose(contours);
                hierarchy?.Dispose();
            }

            return null;
        }

        /// <summary>
        /// 提取相機區域（排除UI）
        /// </summary>
        private Mat ExtractCameraArea(Mat frameMat, Rectangle? uiExcludeRect, out int offsetY)
        {
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                offsetY = 0; // UI排除區域從頂部開始，無偏移
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                // 關鍵修正：正確計算相機區域的起始Y座標
                var totalHeight = frameMat.Height;
                var uiHeight = _redBarSettings.UiHeightFromBottom;
                var cameraHeight = Math.Max(totalHeight - uiHeight, totalHeight / 2);

                // 如果是從底部排除UI，相機區域從頂部開始，偏移為0
                offsetY = 0;
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
        }

        /// <summary>
        /// 轉換HSV值為OpenCV格式
        /// </summary>
        private Scalar ToOpenCvHsv((int h, int s, int v) hsv)
        {
            return new Scalar(hsv.h, hsv.s, hsv.v);
        }

        #endregion

        #region 怪物檢測

        /// <summary>
        /// 非同步處理幀 - 返回結果版本
        /// </summary>
        public async Task<List<MonsterRenderInfo>> ProcessFrameAsync(Bitmap frame, AppConfig config, List<Rectangle>? detectionBoxes = null)
        {
            lock (_processingLock)
            {
                if (_isProcessing || !HasTemplates)
                    return new List<MonsterRenderInfo>();
                _isProcessing = true;
            }

            try
            {
                // ✅ 添加 null 檢查
                if (frame == null || frame.Width == 0 || frame.Height == 0)
                {
                    return new List<MonsterRenderInfo>();
                }

                var results = await Task.Run(() => ProcessMonsterDetection(frame, config, detectionBoxes));

                if (results.Any())
                {
                    _eventHandler.OnStatusMessage($"🎯 怪物: {results.Count}個");
                }

                return results;
            }
            catch (Exception ex)
            {
                _eventHandler.OnStatusMessage($"❌ 怪物識別失敗: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"ProcessFrameAsync 詳細錯誤: {ex}");
                return new List<MonsterRenderInfo>();
            }
            finally
            {
                lock (_processingLock)
                {
                    _isProcessing = false;
                }
            }
        }

        /// <summary>
        /// 實際的怪物識別邏輯 - 支援檢測框限制
        /// </summary>
        private List<MonsterRenderInfo> ProcessMonsterDetection(Bitmap frame, AppConfig config, List<Rectangle>? detectionBoxes = null)
        {
            var detectionSettings = config?.Templates?.MonsterDetection;
            if (detectionSettings == null) return new List<MonsterRenderInfo>();

            var detectionMode = ParseDetectionMode(detectionSettings.DetectionMode);
            int maxAllowedResults = detectionSettings.MaxDetectionResults;
            var allResults = new List<MatchResult>();

            // 檢測框內辨識
            if (detectionBoxes?.Any() == true)
            {
                foreach (var detectionBox in detectionBoxes)
                {
                    using var croppedFrame = CropFrame(frame, detectionBox);
                    if (croppedFrame == null) continue;

                    // ✅ 修復1：使用所有模板，不限制數量
                    var results = TemplateMatcher.FindMonstersWithCache(
                        croppedFrame,
                        _currentTemplates, // 使用所有模板
                        detectionMode,
                        detectionSettings.DefaultThreshold,
                        CurrentMonsterName ?? "Unknown");

                    // ✅ 修復2：不使用過濾，直接調整座標
                    foreach (var result in results)
                    {
                        result.Position = new System.Drawing.Point(
                            result.Position.X + detectionBox.X,
                            result.Position.Y + detectionBox.Y);
                    }

                    allResults.AddRange(results);
                }
            }
            else
            {
                // 全螢幕辨識
                allResults = TemplateMatcher.FindMonstersWithCache(
                    frame,
                    _currentTemplates, // 使用所有模板
                    detectionMode,
                    detectionSettings.DefaultThreshold,
                    CurrentMonsterName ?? "Unknown");
            }

            // ✅ 修復3：使用原版的簡單數量檢查
            if (allResults.Count > maxAllowedResults)
                return new List<MonsterRenderInfo>();

            var finalResults = allResults.Select(r => new MonsterRenderInfo
            {
                Location = r.Position,
                Size = r.Size,
                MonsterName = r.Name,
                Confidence = r.Confidence
            }).ToList();

            return finalResults;
        }

        private MonsterDetectionMode ParseDetectionMode(string modeString)
        {
            // 從設定檔獲取映射
            var config = _eventHandler.ConfigurationManager?.CurrentConfig;
            var modeMapping = config?.DetectionModes?.ModeMapping;

            if (modeMapping?.TryGetValue(modeString, out var mappedMode) == true)
            {
                return Enum.TryParse<MonsterDetectionMode>(mappedMode, out var result)
                    ? result
                    : MonsterDetectionMode.Color;
            }

            // 回退到預設模式
            var defaultMode = config?.DetectionModes?.DefaultMode ?? "Color";
            return Enum.TryParse<MonsterDetectionMode>(defaultMode, out var defaultResult)
                ? defaultResult
                : MonsterDetectionMode.Color;
        }

        /// <summary>
        /// 裁切幀到指定矩形區域
        /// </summary>
        private Bitmap? CropFrame(Bitmap originalFrame, Rectangle cropRect)
        {
            try
            {
                // 確保裁切區域在圖像範圍內
                var validRect = Rectangle.Intersect(cropRect, new Rectangle(0, 0, originalFrame.Width, originalFrame.Height));
                if (validRect.IsEmpty || validRect.Width < 10 || validRect.Height < 10)
                    return null;

                return originalFrame.Clone(validRect, originalFrame.PixelFormat);
            }
            catch (Exception ex)
            {
                _eventHandler.OnStatusMessage($"裁切幀失敗: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void Dispose()
        {
            _monsterComboBox.SelectedIndexChanged -= OnMonsterSelectionChanged;
            ClearCurrentTemplates();
        }
    }
}
