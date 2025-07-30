using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 怪物模板服務 - 負責怪物模板的載入、管理和UI綁定
    /// </summary>
    public class MonsterTemplateService : IDisposable
    {
        private readonly IMonsterTemplateEventHandler _eventHandler;
        private readonly ComboBox _monsterComboBox;
        private List<Bitmap> _currentTemplates;

        public List<Bitmap> CurrentTemplates => _currentTemplates.AsReadOnly().ToList();
        public bool HasTemplates => _currentTemplates.Any();
        public string? CurrentMonsterName { get; private set; }

        public MonsterTemplateService(ComboBox monsterComboBox, IMonsterTemplateEventHandler eventHandler)
        {
            _monsterComboBox = monsterComboBox ?? throw new ArgumentNullException(nameof(monsterComboBox));
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _currentTemplates = new List<Bitmap>();

            // 綁定下拉選單事件
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

                // 獲取所有子資料夾（每個資料夾代表一隻怪物）
                var monsterFolders = Directory.GetDirectories(monstersDirectory);

                if (!monsterFolders.Any())
                {
                    _eventHandler.OnStatusMessage("未找到任何怪物模板資料夾");
                    return;
                }

                foreach (var folder in monsterFolders)
                {
                    // 只取資料夾的名稱作為選項
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
                // 清理現有模板
                ClearCurrentTemplates();

                string monsterFolderPath = Path.Combine(_eventHandler.GetMonstersDirectory(), monsterName);

                if (!Directory.Exists(monsterFolderPath))
                {
                    _eventHandler.OnError($"找不到怪物資料夾: {monsterFolderPath}");
                    return;
                }

                _eventHandler.OnStatusMessage($"正在從 '{monsterName}' 載入怪物模板...");

                // 尋找所有PNG圖片檔案
                var templateFiles = Directory.GetFiles(monsterFolderPath, "*.png");

                if (!templateFiles.Any())
                {
                    _eventHandler.OnStatusMessage($"⚠️ 在 '{monsterName}' 資料夾中未找到任何PNG模板檔案");
                    return;
                }

                // 載入每張圖片
                foreach (var file in templateFiles)
                {
                    try
                    {
                        // 使用 using 和 new Bitmap(tempBitmap) 的方式來讀取，避免鎖定檔案
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
        /// 清理當前載入的模板
        /// </summary>
        public void ClearCurrentTemplates()
        {
            foreach (var template in _currentTemplates)
            {
                template?.Dispose();
            }
            _currentTemplates.Clear();
            CurrentMonsterName = null;
        }

        /// <summary>
        /// 獲取指定索引的模板圖片
        /// </summary>
        public Bitmap? GetTemplate(int index)
        {
            if (index < 0 || index >= _currentTemplates.Count)
                return null;

            return _currentTemplates[index];
        }

        /// <summary>
        /// 獲取所有可用的怪物名稱
        /// </summary>
        public string[] GetAvailableMonsters()
        {
            try
            {
                string monstersDirectory = _eventHandler.GetMonstersDirectory();
                if (!Directory.Exists(monstersDirectory))
                    return Array.Empty<string>();

                var monsterFolders = Directory.GetDirectories(monstersDirectory);
                return monsterFolders.Select(folder => new DirectoryInfo(folder).Name).ToArray();
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"獲取怪物列表失敗: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 設定選中的怪物
        /// </summary>
        public void SelectMonster(string monsterName)
        {
            if (_monsterComboBox.Items.Contains(monsterName))
            {
                _monsterComboBox.SelectedItem = monsterName;
            }
        }

        /// <summary>
        /// 下拉選單選擇變更事件處理
        /// </summary>
        private void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (_monsterComboBox.SelectedItem == null) return;

            string selectedMonster = _monsterComboBox.SelectedItem.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedMonster))
            {
                LoadMonsterTemplates(selectedMonster);
            }
        }

        /// <summary>
        /// 重新整理怪物選項（當有新的怪物資料夾時使用）
        /// </summary>
        public void RefreshMonsterOptions()
        {
            var currentSelection = _monsterComboBox.SelectedItem?.ToString();
            InitializeMonsterDropdown();

            // 恢復之前的選擇
            if (!string.IsNullOrEmpty(currentSelection) && _monsterComboBox.Items.Contains(currentSelection))
            {
                _monsterComboBox.SelectedItem = currentSelection;
            }
        }

        public void Dispose()
        {
            // 解除事件綁定
            _monsterComboBox.SelectedIndexChanged -= OnMonsterSelectionChanged;

            // 清理模板資源
            ClearCurrentTemplates();
        }
    }
}
