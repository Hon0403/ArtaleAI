using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArtaleAI.Models.Map
{
    /// <summary>
    /// 平台線段資料，代表地圖上一條可通行的水平線段幾何。
    /// </summary>
    public class PlatformSegmentData
    {
        /// <summary>平台唯一識別碼</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>起點 X 座標</summary>
        public float X1 { get; set; }

        /// <summary>終點 X 座標</summary>
        public float X2 { get; set; }

        /// <summary>平台高度 Y 座標</summary>
        public float Y { get; set; }
    }

    /// <summary>
    /// 折線平台頂點資料。
    /// </summary>
    public class PlatformPointData
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    /// <summary>
    /// 折線平台資料，代表地圖上一條可通行的多點折線幾何。
    /// </summary>
    public class PolylinePlatformData
    {
        /// <summary>平台唯一識別碼</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>折線點序列</summary>
        public List<PlatformPointData> Points { get; set; } = new();
    }

    /// <summary>
    /// 地圖資料
    /// 儲存地圖上的所有路徑點、區域標記等資訊
    /// </summary>
    public class MapData
    {
        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();

        /// <summary>平台幾何線段列表</summary>
        public List<PlatformSegmentData> Platforms { get; set; } = new();

        /// <summary>折線平台幾何列表</summary>
        public List<PolylinePlatformData> PolylinePlatforms { get; set; } = new();

        /// <summary>
        /// 確保舊的 Platforms 欄位在序列化為 JSON 時被忽略，但反序列化時仍能正常讀取。
        /// </summary>
        public bool ShouldSerializePlatforms() => false;

        /// <summary>導航圖節點（由幾何自動推導生成，不進行 JSON 持久化以避免 Double SSOT）。</summary>
        [JsonIgnore]
        public List<NavNodeData> Nodes { get; set; } = new();

        /// <summary>導航圖邊（由幾何與手動邊自動推導生成，不進行 JSON 持久化以避免 Double SSOT）。</summary>
        [JsonIgnore]
        public List<NavEdgeData> Edges { get; set; } = new();

        /// <summary>手動例外邊列表</summary>
        public List<NavEdgeData> ManualEdges { get; set; } = new();

        /// <summary>安全區列表</summary>
        public List<float[]> SafeZones { get; set; } = new();

        /// <summary>禁制區列表</summary>
        public List<float[]> RestrictedZones { get; set; } = new();
    }

    public class NavNodeData
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public string Type { get; set; } = "Platform";

        /// <summary>編輯器 UI 動作碼（與下拉選單一致）；導航仍以 <see cref="NavEdgeData"/> 為準。</summary>
        public int EditorActionCode { get; set; }
    }

    public class NavEdgeData
    {
        public string FromNodeId { get; set; } = "";
        public string ToNodeId { get; set; } = "";
        public ArtaleAI.Core.Domain.Navigation.NavigationActionType ActionType { get; set; }
        public float Cost { get; set; }
        public List<string> InputSequence { get; set; } = new();
    }
}
