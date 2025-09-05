using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;

namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 定義了所有編輯模式的種類。
    /// </summary>
    public enum EditMode
    {
        None,
        Waypoint,       // ● 路線標記
        SafeZone,       // 🟩 安全區域
        RestrictedZone,  // 🟥 禁止區域
        Rope,           // 🧗 繩索路徑
        Delete          // ❌ 刪除標記
    }

    /// <summary>
    /// 所有地圖標記物件的基礎介面。
    /// </summary>
    public interface IMapObject
    {
        public Guid Id { get; }
    }

    /// <summary>
    /// 代表一條由多個 Waypoint 組成的連續路徑。
    /// </summary>
    public class MapPath : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public List<Waypoint> Points { get; set; } = new List<Waypoint>();
    }


    /// <summary>
    /// 代表一個路徑點。
    /// </summary>
    public class Waypoint : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public PointF Position { get; set; }
    }

    /// <summary>
    /// 代表一個多邊形區域（例如可行走、不可進入）。
    /// </summary>
    public class MapArea : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public List<PointF> Points { get; set; } = new List<PointF>();
    }

    /// <summary>
    /// 代表一條繩索或梯子路徑。
    /// </summary>
    public class Rope : IMapObject
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public PointF Start { get; set; }
        public PointF End { get; set; }
    }
}
