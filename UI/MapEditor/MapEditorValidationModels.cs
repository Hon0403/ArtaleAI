using ArtaleAI.Models.Map;

namespace ArtaleAI.UI.MapEditor
{
    public enum MapEditorValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public enum MapEditorValidationTargetKind
    {
        None,
        Platform,
        Rope,
        JumpLink,
        ManualEdge
    }

    public sealed class MapEditorValidationIssue
    {
        public string Code { get; init; } = string.Empty;
        public MapEditorValidationSeverity Severity { get; init; }
        public string Message { get; init; } = string.Empty;
        public MapEditorValidationTargetKind TargetKind { get; init; } = MapEditorValidationTargetKind.None;
        public PolylinePlatformData? TargetPlatform { get; init; }
        public int TargetRopeIndex { get; init; } = -1;
        public int TargetJumpLinkIndex { get; init; } = -1;
        public ManualEdgeAnchor? TargetManualEdge { get; init; }
    }

    public sealed class MapEditorValidationResult
    {
        public static MapEditorValidationResult Empty { get; } = new();

        public IReadOnlyList<MapEditorValidationIssue> Issues { get; init; } =
            Array.Empty<MapEditorValidationIssue>();

        public int ConnectedComponentCount { get; init; } = 1;

        public int ErrorCount => Issues.Count(i => i.Severity == MapEditorValidationSeverity.Error);
        public int WarningCount => Issues.Count(i => i.Severity == MapEditorValidationSeverity.Warning);
        public int InfoCount => Issues.Count(i => i.Severity == MapEditorValidationSeverity.Info);

        public bool HasIssues => Issues.Count > 0;
    }
}
