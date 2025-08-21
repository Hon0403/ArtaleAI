using ArtaleAI.Interfaces;

namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 地圖檔案管理器 - 負責地圖檔案的載入、儲存和UI綁定
    /// </summary>
    public class MapFileManager : IDisposable
    {
        private readonly IMainFormEvents _eventHandler;
        private readonly ComboBox _mapFilesComboBox;
        private readonly MapEditor _mapEditor;
        private string? _currentMapFilePath;

        public string? CurrentMapFilePath => _currentMapFilePath;
        public bool HasCurrentMap => !string.IsNullOrEmpty(_currentMapFilePath);
        public string? CurrentMapFileName => HasCurrentMap ? Path.GetFileName(_currentMapFilePath) : null;

        public MapFileManager(ComboBox mapFilesComboBox, MapEditor mapEditor, IMainFormEvents eventHandler)
        {
            _mapFilesComboBox = mapFilesComboBox ?? throw new ArgumentNullException(nameof(mapFilesComboBox));
            _mapEditor = mapEditor ?? throw new ArgumentNullException(nameof(mapEditor));
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));

            // 綁定下拉選單事件
            _mapFilesComboBox.SelectedIndexChanged += OnMapFileSelectionChanged;
        }

        /// <summary>
        /// 初始化地圖檔案下拉選單
        /// </summary>
        public void InitializeMapFilesDropdown()
        {
            try
            {
                _mapFilesComboBox.Items.Clear();
                string mapDataDirectory = _eventHandler.GetMapDataDirectory();

                // 確保資料夾存在
                if (!Directory.Exists(mapDataDirectory))
                {
                    Directory.CreateDirectory(mapDataDirectory);
                    _eventHandler.OnStatusMessage($"✅ 已建立地圖資料夾: {mapDataDirectory}");
                }

                // 載入所有地圖檔案
                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.mappath");

                if (!mapFiles.Any())
                {
                    _eventHandler.OnStatusMessage("未找到任何地圖檔案");
                    return;
                }

                foreach (var file in mapFiles)
                {
                    _mapFilesComboBox.Items.Add(Path.GetFileName(file));
                }

                _eventHandler.OnStatusMessage($"✅ 成功載入 {mapFiles.Length} 個地圖檔案選項");
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"初始化地圖檔案下拉選單失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 載入指定的地圖檔案
        /// </summary>
        public void LoadMapFile(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    _eventHandler.OnError("檔案名稱不能為空");
                    return;
                }

                string mapFilePath = Path.Combine(_eventHandler.GetMapDataDirectory(), fileName);

                if (!File.Exists(mapFilePath))
                {
                    _eventHandler.OnError($"檔案不存在: {mapFilePath}");
                    return;
                }

                _eventHandler.OnStatusMessage($"正在載入地圖檔案: {fileName}");

                MapData? loadedData = MapData.LoadFromFile(mapFilePath);
                if (loadedData != null)
                {
                    _mapEditor.LoadMapData(loadedData);
                    _currentMapFilePath = mapFilePath;

                    _eventHandler.OnMapLoaded(fileName);
                    _eventHandler.UpdateWindowTitle($"地圖編輯器 - {fileName}");
                    _eventHandler.RefreshMinimap();

                    _eventHandler.OnStatusMessage($"✅ 成功載入地圖: {fileName}");
                }
                else
                {
                    _eventHandler.OnError($"載入地圖資料失敗: {fileName}");
                }
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"載入地圖檔案時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 儲存當前地圖
        /// </summary>
        public void SaveCurrentMap()
        {
            try
            {
                if (HasCurrentMap)
                {
                    // 儲存到現有檔案
                    var currentMapData = _mapEditor.GetCurrentMapData();
                    currentMapData.SaveToFile(_currentMapFilePath!);

                    _eventHandler.OnMapSaved(CurrentMapFileName!, false);
                    _eventHandler.OnStatusMessage($"✅ 地圖儲存成功: {CurrentMapFileName}");
                }
                else
                {
                    // 新檔案需要另存新檔
                    SaveMapAs();
                }
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 另存新檔
        /// </summary>
        public void SaveMapAs()
        {
            try
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.InitialDirectory = _eventHandler.GetMapDataDirectory();
                    saveFileDialog.Filter = "地圖路徑檔 (*.mappath)|*.mappath";
                    saveFileDialog.DefaultExt = ".mappath";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        var currentMapData = _mapEditor.GetCurrentMapData();
                        currentMapData.SaveToFile(saveFileDialog.FileName);

                        _currentMapFilePath = saveFileDialog.FileName;
                        string fileName = Path.GetFileName(saveFileDialog.FileName);

                        // 重新整理下拉選單
                        InitializeMapFilesDropdown();
                        _mapFilesComboBox.SelectedItem = fileName;

                        _eventHandler.OnMapSaved(fileName, true);
                        _eventHandler.UpdateWindowTitle($"地圖編輯器 - {fileName}");
                        _eventHandler.OnStatusMessage($"✅ 新地圖儲存成功: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"另存新檔時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立新地圖
        /// </summary>
        public void CreateNewMap()
        {
            try
            {
                // 清空目前檔案路徑
                _currentMapFilePath = null;

                // 載入空的地圖資料
                _mapEditor.LoadMapData(new MapData());

                // 清空下拉選單的選擇
                _mapFilesComboBox.SelectedItem = null;

                _eventHandler.OnNewMapCreated();
                _eventHandler.UpdateWindowTitle("地圖編輯器 - (新地圖)");
                _eventHandler.RefreshMinimap();

                _eventHandler.OnStatusMessage("✅ 已建立新地圖");
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 選擇指定的地圖檔案
        /// </summary>
        public void SelectMapFile(string fileName)
        {
            if (_mapFilesComboBox.Items.Contains(fileName))
            {
                _mapFilesComboBox.SelectedItem = fileName;
            }
        }

        /// <summary>
        /// 獲取所有可用的地圖檔案名稱
        /// </summary>
        public string[] GetAvailableMapFiles()
        {
            try
            {
                string mapDataDirectory = _eventHandler.GetMapDataDirectory();
                if (!Directory.Exists(mapDataDirectory))
                    return Array.Empty<string>();

                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.mappath");
                return mapFiles.Select(file => Path.GetFileName(file)).ToArray();
            }
            catch (Exception ex)
            {
                _eventHandler.OnError($"獲取地圖檔案列表失敗: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 重新整理地圖檔案選項
        /// </summary>
        public void RefreshMapFileOptions()
        {
            var currentSelection = _mapFilesComboBox.SelectedItem?.ToString();
            InitializeMapFilesDropdown();

            // 恢復之前的選擇
            if (!string.IsNullOrEmpty(currentSelection) && _mapFilesComboBox.Items.Contains(currentSelection))
            {
                _mapFilesComboBox.SelectedItem = currentSelection;
            }
        }

        /// <summary>
        /// 檢查當前是否有未儲存的變更
        /// </summary>
        public bool HasUnsavedChanges()
        {
            // 這裡可以實作檢查邏輯，比較當前MapData與檔案內容
            // 暫時返回false，可根據需要擴充
            return false;
        }

        /// <summary>
        /// 下拉選單選擇變更事件處理
        /// </summary>
        private void OnMapFileSelectionChanged(object? sender, EventArgs e)
        {
            if (_mapFilesComboBox.SelectedItem == null) return;

            string selectedFileName = _mapFilesComboBox.SelectedItem.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedFileName))
            {
                LoadMapFile(selectedFileName);
            }
        }

        public void Dispose()
        {
            // 解除事件綁定
            _mapFilesComboBox.SelectedIndexChanged -= OnMapFileSelectionChanged;
        }
    }
}
