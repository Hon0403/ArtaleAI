using ArtaleAI.Config;
using ArtaleAI.UI;
using ArtaleAI.Utils;

namespace ArtaleAI.Services
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

        /// <summary>
        /// 地圖儲存完成事件
        /// 參數：(檔案名稱, 是否為新檔案)
        /// </summary>
        public event Action<string, bool>? MapSaved;
        
        /// <summary>
        /// 地圖載入完成事件
        /// 參數：檔案名稱
        /// </summary>
        public event Action<string>? MapLoaded;
        
        /// <summary>
        /// 錯誤發生事件
        /// 參數：錯誤訊息
        /// </summary>
        public event Action<string>? ErrorOccurred;
        
        /// <summary>
        /// 狀態訊息事件
        /// 參數：狀態訊息
        /// </summary>
        public event Action<string>? StatusMessage;
        
        /// <summary>
        /// 檢查是否有已載入的地圖檔案
        /// </summary>
        public bool HasCurrentMap => !string.IsNullOrEmpty(_currentMapFilePath);
        
        /// <summary>
        /// 取得當前地圖檔案名稱（不含路徑）
        /// </summary>
        public string? CurrentMapFileName => HasCurrentMap ? Path.GetFileName(_currentMapFilePath) : null;

        /// <summary>
        /// 初始化地圖檔案管理器
        /// </summary>
        /// <param name="mapFilesComboBox">地圖檔案選擇下拉選單</param>
        /// <param name="mapEditor">地圖編輯器實例</param>
        /// <param name="mainForm">主視窗實例（用於事件回調）</param>
        public MapFileManager(ComboBox mapFilesComboBox, MapEditor mapEditor, MainForm mainForm)
        {
            _mapFilesComboBox = mapFilesComboBox ?? throw new ArgumentNullException(nameof(mapFilesComboBox));
            _mapEditor = mapEditor ?? throw new ArgumentNullException(nameof(mapEditor));
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));

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
                    MsgLog.ShowStatus(_mainForm.textBox1, $"已建立地圖資料夾: {mapDataDirectory}");
                }

                // 載入所有地圖檔案
                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.json");

                if (!mapFiles.Any())
                {
                    MsgLog.ShowStatus(_mainForm.textBox1, "未找到任何地圖檔案");
                    return;
                }

                foreach (var file in mapFiles)
                {
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                    _mapFilesComboBox.Items.Add(fileNameWithoutExtension);
                }

                MsgLog.ShowStatus(_mainForm.textBox1, $"成功載入 {mapFiles.Length} 個地圖檔案選項");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1, $"初始化地圖檔案下拉選單失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 載入指定的地圖檔案到編輯器
        /// 從 MapData 資料夾中載入 JSON 格式的地圖檔案
        /// </summary>
        /// <param name="fileName">檔案名稱（不含副檔名）</param>
        public void LoadMapFile(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    MsgLog.ShowError(_mainForm.textBox1, "檔案名稱不能為空");
                    return;
                }

                string mapFilePath = Path.Combine(PathManager.MapDataDirectory, $"{fileName}.json");

                if (!File.Exists(mapFilePath))
                {
                    MsgLog.ShowError(_mainForm.textBox1, $"檔案不存在: {mapFilePath}");
                    return;
                }

                MsgLog.ShowStatus(_mainForm.textBox1, $"正在載入地圖檔案: {fileName}");

                MapData? loadedData = AppConfig.Instance.LoadMapFromFile(mapFilePath);

                if (loadedData != null)
                {
                    _mapEditor.LoadMapData(loadedData);
                    _currentMapFilePath = mapFilePath;

                    _mainForm.UpdateWindowTitle($"地圖編輯器 - {fileName}");
                    _mainForm.RefreshMinimap();
                    MsgLog.ShowStatus(_mainForm.textBox1, $"成功載入地圖: {fileName}");

                    MapLoaded?.Invoke(fileName);
                }
                else
                {
                    MsgLog.ShowError(_mainForm.textBox1, $"載入地圖資料失敗: {fileName}");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1, $"載入地圖檔案時發生錯誤: {ex.Message}");
            }
        }


        /// <summary>
        /// 儲存當前正在編輯的地圖
        /// 如果沒有已載入的地圖，則會開啟另存新檔對話框
        /// </summary>
        public void SaveCurrentMap()
        {
            try
            {
                if (HasCurrentMap)
                {
                    var currentMapData = _mapEditor.GetCurrentMapData();
                    AppConfig.Instance.SaveMapToFile(currentMapData, _currentMapFilePath!);
                    _mainForm.OnMapSaved(CurrentMapFileName!, false);
                    MsgLog.ShowStatus(_mainForm.textBox1, $"地圖儲存成功: {CurrentMapFileName}");
                }
                else
                {
                    SaveMapAs();
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1, $"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 將當前地圖另存為新檔案
        /// 開啟儲存對話框讓使用者選擇檔案名稱和位置
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

                    AppConfig.Instance.SaveMapToFile(currentMapData, saveFileDialog.FileName);

                    _currentMapFilePath = saveFileDialog.FileName;
                    string fileName = Path.GetFileName(saveFileDialog.FileName);

                    InitializeMapFilesDropdown();
                    _mapFilesComboBox.SelectedItem = fileName;
                    _mainForm.OnMapSaved(fileName, true);
                    _mainForm.UpdateWindowTitle($"地圖編輯器 - {fileName}");
                    MsgLog.ShowStatus(_mainForm.textBox1, $"新地圖儲存成功: {fileName}");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1, $"另存新檔時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立一個空白的新地圖
        /// 清除當前檔案路徑並載入空的地圖資料
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
                MsgLog.ShowStatus(_mainForm.textBox1, "已建立新地圖");

            }
            catch (Exception ex)
            {
                MsgLog.ShowError(_mainForm.textBox1, $"建立新地圖時發生錯誤: {ex.Message}");
            }
        }


        /// <summary>
        /// 在下拉選單中選擇指定的地圖檔案
        /// 如果檔案存在於選單中，會自動觸發載入
        /// </summary>
        /// <param name="fileName">要選擇的檔案名稱</param>
        public void SelectMapFile(string fileName)
        {
            if (_mapFilesComboBox.Items.Contains(fileName))
            {
                _mapFilesComboBox.SelectedItem = fileName;
            }
        }

        /// <summary>
        /// 取得所有可用的地圖檔案名稱列表
        /// 從 MapData 資料夾搜尋所有 .json 檔案
        /// </summary>
        /// <returns>地圖檔案名稱陣列（含副檔名）</returns>
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
                MsgLog.ShowError(_mainForm.textBox1, $"獲取地圖檔案列表失敗: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 重新整理地圖檔案下拉選單選項
        /// 重新掃描 MapData 資料夾並保持當前選擇
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
        /// 目前暫時返回 false，可根據需要擴充實作
        /// </summary>
        /// <returns>如果有未儲存的變更返回 true，否則返回 false</returns>
        public bool HasUnsavedChanges()
        {
            // 這裡可以實作檢查邏輯，比較當前MapData與檔案內容
            // 暫時返回false，可根據需要擴充
            return false;
        }

        /// <summary>
        /// 下拉選單選擇變更事件處理
        /// 當使用者在下拉選單選擇地圖檔案時自動載入
        /// </summary>
        /// <param name="sender">事件來源</param>
        /// <param name="e">事件參數</param>
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
