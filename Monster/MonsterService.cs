using ArtaleAI;
using ArtaleAI.Config;
using ArtaleAI.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArtaleAI.Monster
{
    /// <summary>
    /// 統一的怪物服務 - 整合模板管理和偵測功能
    /// </summary>
    public class MonsterService : IDisposable
    {
        private readonly IApplicationEventHandler _eventHandler;
        private readonly ComboBox _monsterComboBox;
        private List<Bitmap> _currentTemplates;
        private readonly TemplateMatcher _matcher;
        private ConfigManager? _configurationManager;

        public List<Bitmap> CurrentTemplates => _currentTemplates.AsReadOnly().ToList();
        public bool HasTemplates => _currentTemplates.Any();
        public string? CurrentMonsterName { get; private set; }

        public MonsterService(ComboBox monsterComboBox, IApplicationEventHandler eventHandler)
        {
            _monsterComboBox = monsterComboBox ?? throw new ArgumentNullException(nameof(monsterComboBox));
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _currentTemplates = new List<Bitmap>();
            _matcher = new TemplateMatcher();

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
        /// 載入指定怪物的模板（非同步版本）
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

                // 非同步獲取檔案列表
                var templateFiles = await Task.Run(() =>
                    Directory.GetFiles(monsterFolderPath, "*.png"));

                if (!templateFiles.Any())
                {
                    _eventHandler.OnStatusMessage($"在 '{monsterName}' 資料夾中未找到任何PNG模板檔案");
                    return;
                }

                // 非同步載入所有模板圖片
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

                // 等待所有模板載入完成
                var loadedTemplates = await Task.WhenAll(templateTasks);

                // 過濾掉載入失敗的模板
                _currentTemplates.AddRange(loadedTemplates.Where(t => t != null));

                CurrentMonsterName = monsterName;
                _eventHandler.OnTemplatesLoaded(monsterName, _currentTemplates.Count);
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"載入怪物模板時發生錯誤: {ex.Message}");
            }
        }


        /// <summary>
        /// 偵測螢幕上的怪物
        /// </summary>
        public async Task<List<MonsterDetectionResult>> DetectMonstersOnScreenAsync(Bitmap screenImage)
        {
            if (!HasTemplates || screenImage == null)
                return new List<MonsterDetectionResult>();

            var config = (_eventHandler as MainForm)?.ConfigurationManager?.CurrentConfig;
            var detectionSettings = config?.Templates?.MonsterDetection;

            return await Task.Run(() =>
            {
                var results = new List<MonsterDetectionResult>();
                using var screenCopy = new Bitmap(screenImage);

                for (int i = 0; i < _currentTemplates.Count; i++)
                {
                    try
                    {
                        // 使用設定檔中的所有參數
                        var matches = _matcher.FindAllMatches(
                            screenCopy,
                            _currentTemplates[i],
                            detectionSettings.DefaultThreshold,  // 0.01
                            detectionSettings.UseColorFilter,    // 從設定檔讀取
                            detectionSettings.ColorTolerance     // 從設定檔讀取
                        );

                        foreach (var match in matches)
                        {
                            results.Add(new MonsterDetectionResult
                            {
                                MonsterName = CurrentMonsterName ?? "未知",
                                Location = match,
                                Confidence = detectionSettings.DefaultConfidence,
                                TemplateIndex = i,
                                DetectionTime = DateTime.Now
                            });
                        }

                        // 限制結果數量
                        if (results.Count >= detectionSettings.MaxDetectionResults)
                            break;
                    }
                    catch (Exception ex)
                    {
                        // 根據設定檔控制除錯輸出
                        if (detectionSettings.EnableDebugOutput)
                        {
                            System.Diagnostics.Debug.WriteLine($"模板 {i} 匹配失敗: {ex.Message}");
                        }
                    }
                }

                return results;
            });
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
            _matcher?.Dispose();
        }
    }

    /// <summary>
    /// 怪物偵測結果
    /// </summary>
    public class MonsterDetectionResult
    {
        public string MonsterName { get; set; } = string.Empty;
        public Point Location { get; set; }
        public double Confidence { get; set; }
        public int TemplateIndex { get; set; }
        public DateTime DetectionTime { get; set; }
    }
}
