namespace ArtaleAI.UI.MapEditing
{
    public sealed class MapEditorLayerVisibility
    {
        public bool ShowPlatforms { get; set; } = true;
        public bool ShowRopes { get; set; } = true;
        public bool ShowJumpLinks { get; set; } = true;
        public bool ShowManualAnchors { get; set; } = true;
        public bool ShowNodes { get; set; } = true;
        public bool ShowEdges { get; set; } = true;
        public bool ShowValidationOverlays { get; set; } = true;

        public event Action? Changed;

        public void NotifyChanged() => Changed?.Invoke();
    }
}
