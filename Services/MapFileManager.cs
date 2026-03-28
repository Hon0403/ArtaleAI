using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.UI;
using ArtaleAI.Utils;
using System.Text;

namespace ArtaleAI.Services
{
    /// <summary>地圖 JSON 載入／儲存、存檔前完整性驗證與事件通知。</summary>
    public class MapFileManager
    {
        private readonly MapEditor _mapEditor;
        private string? _currentMapFilePath;

        /// <summary>地圖儲存完成事件（檔案名稱, 是否為新檔案）</summary>
        public event Action<string, bool>? MapSaved;

        /// <summary>地圖載入完成事件（檔案名稱）</summary>
        public event Action<string>? MapLoaded;

        /// <summary>狀態訊息事件（取代所有直接 MsgLog 呼叫）</summary>
        public event Action<string>? StatusMessage;

        /// <summary>錯誤訊息事件</summary>
        public event Action<string>? ErrorMessage;

        /// <summary>檔案清單變更事件（當新增/刪除/刷新檔案清單時觸發）</summary>
        public event Action? FileListChanged;

        /// <summary>是否有已載入的地圖檔案</summary>
        public bool HasCurrentMap => !string.IsNullOrEmpty(_currentMapFilePath);

        /// <summary>繩索資料提供者（儲存時合併自動偵測的繩索）</summary>
        public Func<List<float[]>>? RopeDataProvider { get; set; }

        /// <summary>取得當前地圖檔案名稱（不含路徑）</summary>
        public string? CurrentMapFileName => HasCurrentMap ? Path.GetFileName(_currentMapFilePath) : null;

        public MapFileManager(MapEditor mapEditor)
        {
            _mapEditor = mapEditor ?? throw new ArgumentNullException(nameof(mapEditor));
        }

        /// <summary>回傳 <c>MapData</c> 目錄下所有 <c>.json</c> 檔名（無副檔名）。</summary>
        public string[] GetAvailableMapFiles()
        {
            try
            {
                string mapDataDirectory = PathManager.MapDataDirectory;
                if (!Directory.Exists(mapDataDirectory))
                {
                    Directory.CreateDirectory(mapDataDirectory);
                    return Array.Empty<string>();
                }

                return Directory.GetFiles(mapDataDirectory, "*.json")
                    .Select(file => Path.GetFileNameWithoutExtension(file))
                    .ToArray();
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke($"獲取地圖檔案列表失敗: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>自 MapData 目錄載入 JSON 並餵入編輯器。</summary>
        public void LoadMapFile(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName) || fileName == "null") return;

                string mapFilePath = Path.Combine(PathManager.MapDataDirectory, $"{fileName}.json");

                if (!File.Exists(mapFilePath))
                {
                    ErrorMessage?.Invoke($"檔案不存在: {mapFilePath}");
                    return;
                }

                StatusMessage?.Invoke($"正在載入地圖檔案: {fileName}");
                ArtaleAI.Models.Map.MapData? loadedData = LoadMapFromFile(mapFilePath);

                if (loadedData != null)
                {
                    _mapEditor.LoadMapData(loadedData);
                    _currentMapFilePath = mapFilePath;
                    StatusMessage?.Invoke($"成功載入地圖: {fileName}");

                    MapLoaded?.Invoke(fileName);
                }
                else
                {
                    ErrorMessage?.Invoke($"載入地圖資料失敗: {fileName}");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke($"載入地圖檔案時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>若有目前檔案路徑則驗證後覆寫存檔。</summary>
        public void SaveCurrentMap()
        {
            try
            {
                if (HasCurrentMap)
                {
                    var currentMapData = _mapEditor.GetCurrentMapData();
                    MergeDetectedRopes(currentMapData);
                    if (!TryValidateMapIntegrity(currentMapData, out var validationErrors))
                    {
                        ErrorMessage?.Invoke(BuildValidationErrorMessage(validationErrors));
                        return;
                    }
                    SaveMapToFile(currentMapData, _currentMapFilePath!);

                    StatusMessage?.Invoke($"地圖儲存成功: {CurrentMapFileName}");
                    MapSaved?.Invoke(CurrentMapFileName!, false);
                }
                else
                {
                    StatusMessage?.Invoke("尚未載入地圖，請使用另存新檔");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke($"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>驗證後寫入指定路徑並更新目前檔案與事件。</summary>
        public void SaveMapToPath(string filePath)
        {
            try
            {
                var currentMapData = _mapEditor.GetCurrentMapData();
                MergeDetectedRopes(currentMapData);
                if (!TryValidateMapIntegrity(currentMapData, out var validationErrors))
                {
                    ErrorMessage?.Invoke(BuildValidationErrorMessage(validationErrors));
                    return;
                }
                SaveMapToFile(currentMapData, filePath);

                _currentMapFilePath = filePath;
                string fileName = Path.GetFileName(filePath);

                StatusMessage?.Invoke($"新地圖儲存成功: {fileName}");
                MapSaved?.Invoke(fileName, true);
                FileListChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke($"另存新檔時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>清空目前路徑並在編輯器載入空白 <see cref="MapData"/>。</summary>
        public void CreateNewMap()
        {
            try
            {
                _currentMapFilePath = null;
                _mapEditor.LoadMapData(new MapData());

                StatusMessage?.Invoke("已建立新地圖");
                MapLoaded?.Invoke("(新地圖)");
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke($"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        private void MergeDetectedRopes(ArtaleAI.Models.Map.MapData mapData)
        {
            if (RopeDataProvider == null) return;

            var detectedRopes = RopeDataProvider();
            if (detectedRopes == null || detectedRopes.Count == 0) return;

            foreach (var rope in detectedRopes)
            {
                if (!mapData.Ropes.Any(existing =>
                    Math.Abs(existing[0] - rope[0]) < 3 &&
                    Math.Abs(existing[1] - rope[1]) < 3))
                {
                    mapData.Ropes.Add(rope);
                }
            }
        }

        private static bool TryValidateMapIntegrity(MapData mapData, out List<string> errors)
        {
            errors = new List<string>();
            if (mapData.Nodes == null || mapData.Edges == null) return true;

            var nodeById = mapData.Nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .GroupBy(n => n.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            if (nodeById.Count == 0 || mapData.Edges.Count == 0) return true;

            foreach (var edge in mapData.Edges)
            {
                if (!IsDirectionalJump(edge.ActionType)) continue;
                if (!TryGetNodes(nodeById, edge, out var fromNode, out var toNode)) continue;

                float dx = toNode.X - fromNode.X;
                if (Math.Abs(dx) < 0.01f)
                {
                    errors.Add($"[方向錯誤] {edge.FromNodeId} -> {edge.ToNodeId} 為 {edge.ActionType}，但兩點 X 相同。");
                    continue;
                }

                bool isDirectionConsistent =
                    (edge.ActionType == NavigationActionType.JumpLeft && dx < 0) ||
                    (edge.ActionType == NavigationActionType.JumpRight && dx > 0);

                if (!isDirectionConsistent)
                {
                    var expected = dx < 0 ? NavigationActionType.JumpLeft : NavigationActionType.JumpRight;
                    errors.Add($"[方向錯誤] {edge.FromNodeId} -> {edge.ToNodeId} 為 {edge.ActionType}，幾何方向應為 {expected}。");
                }
            }

            foreach (var edge in mapData.Edges)
            {
                if (!IsDirectionalJump(edge.ActionType)) continue;
                if (!TryGetNodes(nodeById, edge, out var fromNode, out var toNode)) continue;

                float reverseDx = fromNode.X - toNode.X;
                if (Math.Abs(reverseDx) < 0.01f) continue;

                var expectedReverse = reverseDx < 0 ? NavigationActionType.JumpLeft : NavigationActionType.JumpRight;
                bool hasReverse = mapData.Edges.Any(e =>
                    string.Equals(e.FromNodeId, edge.ToNodeId, StringComparison.Ordinal) &&
                    string.Equals(e.ToNodeId, edge.FromNodeId, StringComparison.Ordinal) &&
                    e.ActionType == expectedReverse);

                if (!hasReverse)
                {
                    errors.Add($"[缺反向邊] {edge.FromNodeId} -> {edge.ToNodeId} ({edge.ActionType}) 缺少 {edge.ToNodeId} -> {edge.FromNodeId} ({expectedReverse})。");
                }
            }

            return errors.Count == 0;
        }

        private static bool IsDirectionalJump(NavigationActionType actionType)
        {
            return actionType == NavigationActionType.JumpLeft || actionType == NavigationActionType.JumpRight;
        }

        private static bool TryGetNodes(
            Dictionary<string, NavNodeData> nodeById,
            NavEdgeData edge,
            out NavNodeData fromNode,
            out NavNodeData toNode)
        {
            fromNode = null!;
            toNode = null!;
            if (!nodeById.TryGetValue(edge.FromNodeId, out fromNode!))
            {
                return false;
            }

            if (!nodeById.TryGetValue(edge.ToNodeId, out toNode!))
            {
                return false;
            }

            return true;
        }

        private static string BuildValidationErrorMessage(IReadOnlyList<string> errors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("存檔已阻擋：地圖邊完整性檢查未通過。");

            foreach (var error in errors.Take(20))
            {
                sb.AppendLine($"- {error}");
            }

            if (errors.Count > 20)
            {
                sb.AppendLine($"- ... 其餘 {errors.Count - 20} 項錯誤請先修正前述項目後再存檔。");
            }

            sb.Append("請先修正 Jump 方向或補齊反向邊，再重新存檔。");
            return sb.ToString();
        }

        /// <summary>反序列化 JSON；失敗或不存在時回傳 null。</summary>
        public static ArtaleAI.Models.Map.MapData? LoadMapFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                string json = File.ReadAllText(filePath);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<ArtaleAI.Models.Map.MapData>(json);
            }
            catch (Exception ex)
            {
                Logger.Error($"[地圖] 載入檔案失敗 {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>縮排 JSON 寫入檔案。</summary>
        public static void SaveMapToFile(ArtaleAI.Models.Map.MapData mapData, string filePath)
        {
            try
            {
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(mapData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"[地圖] 儲存檔案失敗 {filePath}: {ex.Message}");
            }
        }
    }
}
