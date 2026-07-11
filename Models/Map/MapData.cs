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
        public ArtaleAI.Core.Domain.Navigation.NavigationActionType ActionType { get; set; } =
            ArtaleAI.Core.Domain.Navigation.NavigationActionType.Walk;
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
        public string? PlatformId { get; set; }
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
