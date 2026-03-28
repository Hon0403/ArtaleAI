namespace ArtaleAI.UI.MapEditing
{
    /// <summary>小地圖使用情境（路徑編輯／即時疊加）。</summary>
    public enum MinimapUsage
    {
        PathEditing,
        LiveViewOverlay
    }

    /// <summary>地圖編輯互動模式。</summary>
    public enum EditMode
    {
        None,
        Waypoint,
        Rope,
        Delete,
        Select,
        Link
    }
}
