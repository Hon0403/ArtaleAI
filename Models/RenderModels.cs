using System.Drawing;

namespace ArtaleAI.Models
{
    /// <summary>
    /// 怪物渲染資訊
    /// </summary>
    public class MonsterRenderInfo
    {
        public Point Location { get; set; }
        public Size Size { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public double Confidence { get; set; }

        /// <summary>
        /// 獲取渲染矩形
        /// </summary>
        public Rectangle GetRenderRectangle()
        {
            return new Rectangle(Location.X, Location.Y, Size.Width, Size.Height);
        }

        /// <summary>
        /// 獲取中心點座標
        /// </summary>
        public Point GetCenterPoint()
        {
            return new Point(
                Location.X + Size.Width / 2,
                Location.Y + Size.Height / 2
            );
        }
    }
}
