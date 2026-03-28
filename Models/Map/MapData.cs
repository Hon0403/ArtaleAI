using System;
using System.Collections.Generic;

namespace ArtaleAI.Models.Map
{
    /// <summary>
    /// 地圖資料
    /// 儲存地圖上的所有路徑點、區域標記等資訊
    /// </summary>
    public class MapData
    {
        /// <summary>路徑點列表（用於向下相容舊版格式）</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<float[]>? WaypointPaths { get; set; } = null;

        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();

        /// <summary>自定義連接關係 (向後相容舊版格式)</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<int[]>? Connections { get; set; } = null;

        /// <summary>新版導航圖節點</summary>
        public List<NavNodeData> Nodes { get; set; } = new();

        /// <summary>新版導航圖邊</summary>
        public List<NavEdgeData> Edges { get; set; } = new();

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
