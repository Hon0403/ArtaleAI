using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ArtaleAI;

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

                _eventHandler.OnStatusMessage($"✅ 成功載入 {monsterFolders.Length} 種怪物模板選項");
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"初始化怪物下拉選單失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 載入指定怪物的模板
        /// </summary>
        public void LoadMonsterTemplates(string monsterName)
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

                var templateFiles = Directory.GetFiles(monsterFolderPath, "*.png");
                if (!templateFiles.Any())
                {
                    _eventHandler.OnStatusMessage($"⚠️ 在 '{monsterName}' 資料夾中未找到任何PNG模板檔案");
                    return;
                }

                foreach (var file in templateFiles)
                {
                    try
                    {
                        using (var tempBitmap = new Bitmap(file))
                        {
                            _currentTemplates.Add(new Bitmap(tempBitmap));
                        }
                    }
                    catch (Exception ex)
                    {
                        _eventHandler.OnStatusMessage($"⚠️ 載入模板檔案失敗: {Path.GetFileName(file)} - {ex.Message}");
                    }
                }

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
        public List<MonsterDetectionResult> DetectMonstersOnScreen(Bitmap screenImage)
        {
            if (!HasTemplates || screenImage == null)
                return new List<MonsterDetectionResult>();

            var results = new List<MonsterDetectionResult>();

            for (int i = 0; i < _currentTemplates.Count; i++)
            {
                var matches = _matcher.FindAllMatches(screenImage, _currentTemplates[i], 0.7);

                foreach (var match in matches)
                {
                    results.Add(new MonsterDetectionResult
                    {
                        MonsterName = CurrentMonsterName ?? "未知",
                        Location = match,
                        Confidence = 0.8, // 簡化實作
                        TemplateIndex = i,
                        DetectionTime = DateTime.Now
                    });
                }
            }

            return results;
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

        private void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (_monsterComboBox.SelectedItem == null) return;

            string selectedMonster = _monsterComboBox.SelectedItem.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedMonster))
            {
                LoadMonsterTemplates(selectedMonster);
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
