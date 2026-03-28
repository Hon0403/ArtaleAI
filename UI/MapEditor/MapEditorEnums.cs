namespace ArtaleAI.UI.MapEditing
{
    /// <summary>
    /// 小地圖的使用情境模式
    /// </summary>
    public enum MinimapUsage
    {
        PathEditing,
        LiveViewOverlay
    }

    /// <summary>
    /// 定義所有編輯模式的種類
    /// </summary>
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
