using ArtaleAI.Models.Map;
using System.Drawing;

namespace ArtaleAI.UI.MapEditing
{
    public enum MapEditorSelectionKind
    {
        None,
        Platform,
        Rope,
        ManualEdge,
        RuntimeNode
    }

    /// <summary>地圖編輯器目前選取狀態；runtime 節點僅供唯讀檢視。</summary>
    public sealed class MapEditorSelection
    {
        public static MapEditorSelection Empty { get; } = new();

        public MapEditorSelectionKind Kind { get; init; } = MapEditorSelectionKind.None;
        public PolylinePlatformData? Platform { get; init; }
        public int RopeIndex { get; init; } = -1;
        public ManualEdgeAnchor? ManualEdge { get; init; }
        public int RuntimeNodeIndex { get; init; } = -1;
        public int SegmentIndex { get; init; } = -1;
        public PointF ProjectionPoint { get; init; }

        public bool IsEmpty => Kind == MapEditorSelectionKind.None;

        public static MapEditorSelection ForPlatform(
            PolylinePlatformData platform,
            int segmentIndex = -1,
            PointF projectionPoint = default) =>
            new()
            {
                Kind = MapEditorSelectionKind.Platform,
                Platform = platform,
                SegmentIndex = segmentIndex,
                ProjectionPoint = projectionPoint
            };

        public static MapEditorSelection ForRope(int ropeIndex) =>
            new()
            {
                Kind = MapEditorSelectionKind.Rope,
                RopeIndex = ropeIndex
            };

        public static MapEditorSelection ForManualEdge(ManualEdgeAnchor anchor) =>
            new()
            {
                Kind = MapEditorSelectionKind.ManualEdge,
                ManualEdge = anchor
            };

        public static MapEditorSelection ForRuntimeNode(int nodeIndex) =>
            new()
            {
                Kind = MapEditorSelectionKind.RuntimeNode,
                RuntimeNodeIndex = nodeIndex
            };
    }

    public sealed class MapEditorHoverInfo
    {
        public static MapEditorHoverInfo Empty { get; } = new();

        public int SegmentIndex { get; init; } = -1;
        public PointF ProjectionPoint { get; init; }
        public int RuntimeNodeIndex { get; init; } = -1;
        public bool HasRuntimeNode => RuntimeNodeIndex >= 0;
        public bool HasSegmentContext => SegmentIndex >= 0;
        public bool HasProjection =>
            ProjectionPoint != PointF.Empty &&
            !float.IsNaN(ProjectionPoint.X) &&
            !float.IsNaN(ProjectionPoint.Y);
    }
}
