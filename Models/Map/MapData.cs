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
        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();

        /// <summary>導航圖節點（座標與類型；導航與編輯器 SSOT）。</summary>
        public List<NavNodeData> Nodes { get; set; } = new();

        /// <summary>導航圖邊（有向）。</summary>
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
