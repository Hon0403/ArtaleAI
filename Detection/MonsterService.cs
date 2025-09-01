using ArtaleAI.Config;
using ArtaleAI.Interfaces;
using ArtaleAI.Models;


namespace ArtaleAI.Detection
{
    /// <summary>
    /// 統一的怪物服務 - 整合模板管理和偵測功能 (OpenCvSharp 版本)
    /// </summary>
    public class MonsterService : IDisposable
    {
        private readonly IMainFormEvents _eventHandler;
        private readonly ComboBox _monsterComboBox;
        private List<Bitmap> _currentTemplates;
        private bool _isProcessing = false;
        private readonly object _processingLock = new();
        public List<Bitmap> CurrentTemplates => _currentTemplates.AsReadOnly().ToList();
        public bool HasTemplates => _currentTemplates.Any();
        public event Action<List<MonsterRenderInfo>>? MonsterDetected;

        public string? CurrentMonsterName { get; private set; }

        public MonsterService(ComboBox monsterComboBox, IMainFormEvents eventHandler)
        {
            _monsterComboBox = monsterComboBox ?? throw new ArgumentNullException(nameof(monsterComboBox));
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _currentTemplates = new List<Bitmap>();

            _monsterComboBox.SelectedIndexChanged += OnMonsterSelectionChanged;
        }

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

        public Bitmap? GetTemplate(int index)
        {
            if (index < 0 || index >= _currentTemplates.Count)
                return null;

            return _currentTemplates[index];
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

        /// <summary>
        /// 新增：非同步處理幀 - 核心方法
        /// </summary>
        public async Task ProcessFrameAsync(Bitmap frame, AppConfig config, List<Rectangle>? detectionBoxes = null)
        {
            // 檢查是否正在處理，避免堆積
            lock (_processingLock)
            {
                if (_isProcessing || !HasTemplates) return;
                _isProcessing = true;
            }

            try
            {
                // 在背景線程處理
                var results = await Task.Run(() => ProcessMonsterDetection(frame, config, detectionBoxes));
                if (results.Any())
                {
                    // 通知UI更新（在UI線程中執行）
                    MonsterDetected?.Invoke(results);
                    _eventHandler.OnStatusMessage($"🎯 怪物: {results.Count}個");
                }
            }
            catch (Exception ex)
            {
                _eventHandler.OnStatusMessage($"❌ 怪物識別失敗: {ex.Message}");
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
        /// 🆕 修改：實際的怪物識別邏輯 - 支援檢測框限制
        /// </summary>
        private List<MonsterRenderInfo> ProcessMonsterDetection(Bitmap frame, AppConfig config, List<Rectangle>? detectionBoxes = null)
        {
            var detectionSettings = config?.Templates?.MonsterDetection;
            if (detectionSettings == null) return new List<MonsterRenderInfo>();

            var detectionMode = ParseDetectionMode(detectionSettings.DetectionMode);
            int maxAllowedResults = detectionSettings.MaxDetectionResults;
            var allResults = new List<MatchResult>();

            // 🆕 如果有檢測框，只在框內辨識
            if (detectionBoxes?.Any() == true)
            {
                foreach (var detectionBox in detectionBoxes)
                {
                    // 裁切檢測框區域
                    using var croppedFrame = CropFrame(frame, detectionBox);
                    if (croppedFrame == null) continue;

                    var results = TemplateMatcher.FindMonstersWithCache(
                        croppedFrame,
                        _currentTemplates,
                        detectionMode,
                        detectionSettings.DefaultThreshold,
                        CurrentMonsterName ?? "Unknown"
                    );

                    // 🆕 調整座標：將相對於裁切區域的座標轉換為螢幕座標
                    foreach (var result in results)
                    {
                        result.Position = new System.Drawing.Point(
                            result.Position.X + detectionBox.X,
                            result.Position.Y + detectionBox.Y
                        );
                    }

                    allResults.AddRange(results);
                }
            }
            else
            {
                // 原本的全螢幕辨識（向下相容）
                allResults = TemplateMatcher.FindMonstersWithCache(
                    frame,
                    _currentTemplates,
                    detectionMode,
                    detectionSettings.DefaultThreshold,
                    CurrentMonsterName ?? "Unknown"
                );
            }

            if (allResults.Count > maxAllowedResults) return new List<MonsterRenderInfo>();

            return allResults.Select(r => new MonsterRenderInfo
            {
                Location = r.Position,
                Size = r.Size,
                MonsterName = r.Name,
                Confidence = r.Confidence
            }).ToList();
        }

        private MonsterDetectionMode ParseDetectionMode(string modeString)
        {
            // 從設定檔獲取映射
            var config = _eventHandler.ConfigurationManager.CurrentConfig;
            var modeMapping = config?.DetectionModes?.ModeMapping;

            if (modeMapping?.TryGetValue(modeString, out var mappedMode) == true)
            {
                return Enum.TryParse<MonsterDetectionMode>(mappedMode, out var result)
                    ? result
                    : MonsterDetectionMode.Color;
            }

            // 回退到預設模式
            var defaultMode = config.DetectionModes.DefaultMode;
            return Enum.TryParse<MonsterDetectionMode>(defaultMode, out var defaultResult)
                ? defaultResult
                : MonsterDetectionMode.Color;
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

        public void Dispose()
        {
            _monsterComboBox.SelectedIndexChanged -= OnMonsterSelectionChanged;
            ClearCurrentTemplates();
            TemplateMatcher.Dispose();
        }

        /// <summary>
        /// 🆕 裁切幀到指定矩形區域
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
    }
}
