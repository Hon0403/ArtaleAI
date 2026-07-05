using System;
using System.Collections.Generic;

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
    /// 地圖資料
    /// 儲存地圖上的所有路徑點、區域標記等資訊
    /// </summary>
    public class MapData
    {
        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();

        /// <summary>平台幾何線段列表</summary>
        public List<PlatformSegmentData> Platforms { get; set; } = new();

        /// <summary>導航圖節點（座標與類型；導航與編輯器 SSOT）。</summary>
        public List<NavNodeData> Nodes { get; set; } = new();

        /// <summary>導航圖邊（有向）。</summary>
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
