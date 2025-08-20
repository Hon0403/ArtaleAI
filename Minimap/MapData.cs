using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 負責管理一張地圖上的所有標記數據，並處理檔案的儲存與讀取。
    /// </summary>
    public class MapData
    {
        public List<MapPath> WaypointPaths { get; set; } = new List<MapPath>();
        public List<MapArea> SafeZone { get; set; } = new List<MapArea>();
        public List<MapArea> ForbiddenAreas { get; set; } = new List<MapArea>();
        public List<Rope> Ropes { get; set; } = new List<Rope>();

        // 用來儲存單點的「禁止區域」標記
        public List<Waypoint> RestrictedPoints { get; set; } = new List<Waypoint>();

        /// <summary>
        /// 將目前的地圖數據儲存到指定的 JSON 檔案。
        /// </summary>
        public void SaveToFile(string filePath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, jsonString);
        }

        /// <summary>
        /// 從指定的 JSON 檔案載入地圖數據。
        /// </summary>
        public static MapData? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            string jsonString = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<MapData>(jsonString);
        }
    }
}
