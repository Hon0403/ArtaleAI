using ArtaleAI.Models;
using ArtaleAI.Utils;



namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 地圖檔案管理器 - 負責地圖檔案的載入、儲存和UI綁定
    /// </summary>
    public class MapFileManager
    {
        private readonly MainForm _mainForm;
        private readonly ComboBox _mapFilesComboBox;
        private readonly MapEditor _mapEditor;
        private string? _currentMapFilePath;

        public event Action<string, bool>? MapSaved;
        public event Action<string>? MapLoaded;
        public event Action<string>? ErrorOccurred;
        public event Action<string>? StatusMessage;
        public bool HasCurrentMap => !string.IsNullOrEmpty(_currentMapFilePath);
        public string? CurrentMapFileName => HasCurrentMap ? Path.GetFileName(_currentMapFilePath) : null;


        public MapFileManager(ComboBox mapFilesComboBox, MapEditor mapEditor, MainForm eventHandler)
        {
            _mapFilesComboBox = mapFilesComboBox ?? throw new ArgumentNullException(nameof(mapFilesComboBox));
            _mapEditor = mapEditor ?? throw new ArgumentNullException(nameof(mapEditor));
            _mainForm = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));

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
                string mapDataDirectory = PathManager.MapDataDirectory;

                // 確保資料夾存在
                if (!Directory.Exists(mapDataDirectory))
                {
                    Directory.CreateDirectory(mapDataDirectory);
                    MsgLog.ShowStatus(_mainForm.textBox1,$" 已建立地圖資料夾: {mapDataDirectory}");
                }

                // 載入所有地圖檔案
                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.json");

                if (!mapFiles.Any())
                {
                    MsgLog.ShowStatus(_mainForm.textBox1,"未找到任何地圖檔案");
                    return;
                }

                foreach (var file in mapFiles)
                {
                    _mapFilesComboBox.Items.Add(Path.GetFileName(file));
                }

                MsgLog.ShowStatus(_mainForm.textBox1,$" 成功載入 {mapFiles.Length} 個地圖檔案選項");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1,$"初始化地圖檔案下拉選單失敗: {ex.Message}");
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
                    MsgLog.ShowError(_mainForm.textBox1,"檔案名稱不能為空");
                    return;
                }

                string mapFilePath = Path.Combine(PathManager.MapDataDirectory, fileName);
                if (!File.Exists(mapFilePath))
                {
                    MsgLog.ShowError(_mainForm.textBox1,$"檔案不存在: {mapFilePath}");
                    return;
                }

                MsgLog.ShowStatus(_mainForm.textBox1,$"正在載入地圖檔案: {fileName}");

                MapData? loadedData = _mainForm._configManager?.LoadMapFromFile(mapFilePath);

                if (loadedData != null)
                {
                    _mapEditor.LoadMapData(loadedData);
                    _currentMapFilePath = mapFilePath;

                    _mainForm.UpdateWindowTitle($"地圖編輯器 - {fileName}");
                    _mainForm.RefreshMinimap();
                    MsgLog.ShowStatus(_mainForm.textBox1,$"✅ 成功載入地圖: {fileName}");

                    MapLoaded?.Invoke(fileName);
                }
                else
                {
                    MsgLog.ShowError(_mainForm.textBox1,$"載入地圖資料失敗: {fileName}");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1,$"載入地圖檔案時發生錯誤: {ex.Message}");
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
                    var currentMapData = _mapEditor.GetCurrentMapData();
                    _mainForm._configManager?.SaveMapToFile(currentMapData, _currentMapFilePath!);
                    _mainForm.OnMapSaved(CurrentMapFileName!, false);
                    MsgLog.ShowStatus(_mainForm.textBox1,$"✅ 地圖儲存成功: {CurrentMapFileName}");
                }
                else
                {
                    SaveMapAs();
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1,$"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 另存新檔
        /// </summary>
        public void SaveMapAs()
        {
            try
            {
                using var saveFileDialog = new SaveFileDialog
                {
                    InitialDirectory = PathManager.MapDataDirectory,
                    Filter = "地圖路徑檔 (*.json)|*.json",
                    DefaultExt = ".json"
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    var currentMapData = _mapEditor.GetCurrentMapData();

                    _mainForm._configManager?.SaveMapToFile(currentMapData, saveFileDialog.FileName);

                    _currentMapFilePath = saveFileDialog.FileName;
                    string fileName = Path.GetFileName(saveFileDialog.FileName);

                    InitializeMapFilesDropdown();
                    _mapFilesComboBox.SelectedItem = fileName;
                    _mainForm.OnMapSaved(fileName, true);
                    _mainForm.UpdateWindowTitle($"地圖編輯器 - {fileName}");
                    MsgLog.ShowStatus(_mainForm.textBox1,$"✅ 新地圖儲存成功: {fileName}");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1,$"另存新檔時發生錯誤: {ex.Message}");
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

                _mainForm.UpdateWindowTitle("地圖編輯器 - (新地圖)");
                _mainForm.RefreshMinimap();
                MsgLog.ShowStatus(_mainForm.textBox1,"✅ 已建立新地圖");

            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1,$"建立新地圖時發生錯誤: {ex.Message}");
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
                string mapDataDirectory = PathManager.MapDataDirectory;
                if (!Directory.Exists(mapDataDirectory))
                    return Array.Empty<string>();

                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.json");
                return mapFiles.Select(file => Path.GetFileName(file)).ToArray();
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1,$"獲取地圖檔案列表失敗: {ex.Message}");
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
    }
}
