using ArtaleAI;
using ArtaleAI.Config;
using ArtaleAI.Utils;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArtaleAI.Monster
{
    /// <summary>
    /// 統一的怪物服務 - 整合模板管理和偵測功能 (OpenCvSharp 版本)
    /// </summary>
    public class MonsterService : IDisposable
    {
        private readonly IApplicationEventHandler _eventHandler;
        private readonly ComboBox _monsterComboBox;
        private List<Bitmap> _currentTemplates;

        public List<Bitmap> CurrentTemplates => _currentTemplates.AsReadOnly().ToList();
        public bool HasTemplates => _currentTemplates.Any();
        public string? CurrentMonsterName { get; private set; }

        public MonsterService(ComboBox monsterComboBox, IApplicationEventHandler eventHandler)
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
    }
}
