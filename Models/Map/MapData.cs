using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArtaleAI.Models.Map
{
    /// <summary>
    /// 折線平台頂點資料。
    /// </summary>
    public class PlatformPointData
    {
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>
        /// 策略旗標：不改拓撲幾何，僅影響巡邏終點權重等決策。
        /// JSON 省略時視為 false。
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsSafeZone { get; set; }
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
    /// 手動例外邊的幾何錨點。持久化語意為「在某平台的某個位置」，
    /// 不綁定 runtime node ID，由 BuildHTopology.ResolveManualEdgeAnchors() 轉譯。
    /// </summary>
    public class ManualEdgeAnchor
    {
        public string FromPlatformId { get; set; } = string.Empty;
        public float FromX { get; set; }
        public float FromY { get; set; }
        public int? FromSegmentIndex { get; set; }
        public string ToPlatformId { get; set; } = string.Empty;
        public float ToX { get; set; }
        public float ToY { get; set; }
        public int? ToSegmentIndex { get; set; }
        public ArtaleAI.Domain.Navigation.NavigationActionType ActionType { get; set; } =
            ArtaleAI.Domain.Navigation.NavigationActionType.Walk;
    }

    /// <summary>
    /// 地圖資料
    /// 儲存地圖上的所有路徑點、區域標記等資訊
    /// </summary>
    public class MapData
    {
        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();

        /// <summary>
        /// 垂直跳點通道，格式與 <see cref="Ropes"/> 相同：[x, topY, bottomY]。
        /// 拓撲層自動建立 Jump（下→上）與 JumpDown（上→下）雙向邊。
        /// </summary>
        public List<float[]> JumpLinks { get; set; } = new();

        /// <summary>折線平台幾何列表</summary>
        public List<PolylinePlatformData> PolylinePlatforms { get; set; } = new();

        /// <summary>導航圖節點（由幾何自動推導生成，不進行 JSON 持久化以避免 Double SSOT）。</summary>
        [JsonIgnore]
        public List<NavNodeData> Nodes { get; set; } = new();

        /// <summary>導航圖邊（由幾何與手動邊自動推導生成，不進行 JSON 持久化以避免 Double SSOT）。</summary>
        [JsonIgnore]
        public List<NavEdgeData> Edges { get; set; } = new();

        /// <summary>手動例外邊幾何錨點（持久化 SSOT）</summary>
        public List<ManualEdgeAnchor> ManualEdgeAnchors { get; set; } = new();

        /// <summary>
        /// 舊版安全區線段（僅供載入遷移）。新 SSOT 為 <see cref="PlatformPointData.IsSafeZone"/>。
        /// 遷移後設為 null，存檔時省略。
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<float[]>? SafeZones { get; set; }

        /// <summary>
        /// 禁制區列表。格式 <c>[x1, y1, x2, y2]</c>。
        /// 目前執行期未消費；保留為地圖 JSON 資料契約，避免存檔時默默遺失。
        /// </summary>
        public List<float[]> RestrictedZones { get; set; } = new();
    }

    public class NavNodeData
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public string Type { get; set; } = "Platform";
        public string? PlatformId { get; set; }

        /// <summary>由平台折點旗標推導；不持久化。</summary>
        public bool IsSafeZone { get; set; }
    }

    public class NavEdgeData
    {
        public string FromNodeId { get; set; } = "";
        public string ToNodeId { get; set; } = "";
        public ArtaleAI.Domain.Navigation.NavigationActionType ActionType { get; set; }
        public float Cost { get; set; }
        public List<string> InputSequence { get; set; } = new();
    }
}
