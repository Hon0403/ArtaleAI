using ArtaleAI.Utils;
using ArtaleAI.Models;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PathData = ArtaleAI.Models.PathData;

namespace ArtaleAI.Config
{
    public class ConfigManager
    {
        private readonly MainForm _mainForm;
        private static readonly string DefaultPath = UtilityHelper.GetConfigFilePath();
        public AppConfig? CurrentConfig { get; private set; }

        public ConfigManager(MainForm mainForm)
        {
            _mainForm = mainForm;
        }

        #region 載入配置

        public void Load(string? path = null)
        {
            try
            {
                CurrentConfig = LoadFromFile(path);
                _mainForm.OnConfigLoaded(CurrentConfig!);
            }
            catch (Exception ex)
            {
                _mainForm.OnConfigError($"讀取設定檔失敗: {ex.Message}");
            }
        }

        private AppConfig LoadFromFile(string? path = null)
        {
            var configPath = path ?? DefaultPath;
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"找不到設定檔！路徑：{configPath}", configPath);
            }

            var yamlContent = File.ReadAllText(configPath, Encoding.UTF8);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            return deserializer.Deserialize<AppConfig>(yamlContent) ?? new AppConfig();
        }

        #endregion

        #region 儲存配置

        public void Save(string? path = null)
        {
            try
            {
                if (CurrentConfig != null)
                {
                    SaveToFile(CurrentConfig, path);
                    _mainForm.OnConfigSaved(CurrentConfig);
                }
            }
            catch (Exception ex)
            {
                _mainForm.OnConfigError($"儲存設定檔失敗: {ex.Message}");
            }
        }

        private void SaveToFile(AppConfig config, string? path = null)
        {
            var configPath = path ?? DefaultPath;
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var yamlContent = serializer.Serialize(config);
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configPath, yamlContent, Encoding.UTF8);
        }

        #endregion

        #region 配置操作

        public void SetValue(Action<AppConfig> setter, bool autoSave = false)
        {
            if (CurrentConfig != null)
            {
                setter(CurrentConfig);
                if (autoSave) Save();
            }
        }

        #endregion

        #region 地圖檔案操作 - 新增功能

        /// <summary>
        /// 動態序列化地圖資料 - 統一使用 points 格式，只保存有資料的欄位
        /// </summary>
        public void SaveMapToFile(MapData mapData, string filePath)
        {
            var dataToSave = new Dictionary<string, object>();

            // 統一處理：所有模式都使用 points 陣列格式
            if (mapData.WaypointPaths?.Any() == true)
            {
                dataToSave["waypointPaths"] = mapData.WaypointPaths.Select(p => new {
                    points = p.Points.Select(pt => new[] {
                        Math.Round(pt.X, 1),
                        Math.Round(pt.Y, 1)
                    }).ToArray()
                }).ToArray();
            }

            if (mapData.SafeZones?.Any() == true)
            {
                dataToSave["safeZones"] = mapData.SafeZones.Select(z => new {
                    points = z.Points.Select(pt => new[] {
                        Math.Round(pt.X, 1),
                        Math.Round(pt.Y, 1)
                    }).ToArray()
                }).ToArray();
            }

            // 繩索也使用 points 格式（兩個點：起點和終點）
            if (mapData.Ropes?.Any() == true)
            {
                dataToSave["ropes"] = mapData.Ropes.Select(r => new {
                    points = r.Points.Select(pt => new[] {
                        Math.Round(pt.X, 1),
                        Math.Round(pt.Y, 1)
                    }).ToArray()
                }).ToArray();
            }

            if (mapData.RestrictedPoints?.Any() == true)
            {
                dataToSave["restrictedPoints"] = mapData.RestrictedPoints.Select(pt =>
                    new[] { Math.Round(pt.X, 1), Math.Round(pt.Y, 1) }
                ).ToArray();
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(dataToSave, options);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// 從 JSON 檔案載入地圖資料
        /// </summary>
        public MapData? LoadMapFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                return LoadMapFromJson(json);
            }
            catch (Exception ex)
            {
                _mainForm.OnError($"載入地圖檔案失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 從 JSON 字串載入地圖資料
        /// </summary>
        private MapData LoadMapFromJson(string json)
        {
            var jsonDoc = JsonDocument.Parse(json);
            var root = jsonDoc.RootElement;
            var mapData = new MapData();

            // 載入路徑點
            if (root.TryGetProperty("waypointPaths", out var waypointPaths))
            {
                mapData.WaypointPaths = LoadPathDataArray(waypointPaths);
            }

            // 載入安全區域
            if (root.TryGetProperty("safeZones", out var safeZones))
            {
                mapData.SafeZones = LoadPathDataArray(safeZones);
            }

            // 載入繩索（現在也是 points 格式）
            if (root.TryGetProperty("ropes", out var ropes))
            {
                mapData.Ropes = LoadPathDataArray(ropes);
            }

            // 載入限制點
            if (root.TryGetProperty("restrictedPoints", out var restrictedPoints))
            {
                mapData.RestrictedPoints = new List<PointF>();
                foreach (var pointArray in restrictedPoints.EnumerateArray())
                {
                    var coords = pointArray.EnumerateArray().ToArray();
                    if (coords.Length >= 2)
                    {
                        mapData.RestrictedPoints.Add(new PointF(
                            (float)coords[0].GetDouble(),
                            (float)coords[1].GetDouble()
                        ));
                    }
                }
            }

            return mapData;
        }

        /// <summary>
        /// 載入 PathData 陣列的通用方法
        /// </summary>
        private List<PathData> LoadPathDataArray(JsonElement jsonElement)
        {
            var pathList = new List<PathData>();

            foreach (var pathElement in jsonElement.EnumerateArray())
            {
                var path = new PathData();
                if (pathElement.TryGetProperty("points", out var pointsElement))
                {
                    foreach (var pointArray in pointsElement.EnumerateArray())
                    {
                        var coords = pointArray.EnumerateArray().ToArray();
                        if (coords.Length >= 2)
                        {
                            path.Points.Add(new PointF(
                                (float)coords[0].GetDouble(),
                                (float)coords[1].GetDouble()
                            ));
                        }
                    }
                }
                pathList.Add(path);
            }

            return pathList;
        }

        #endregion
    }
}
