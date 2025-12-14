namespace ArtaleAI.Models.PathPlanning
{
    /// <summary>
    /// 平台邊界 - 定義角色可移動的安全範圍
    /// 用於防止角色掉落或超出平台邊界
    /// </summary>
    public class PlatformBounds
    {
        /// <summary>X 軸最小值（左邊界）</summary>
        public float MinX { get; set; }
        
        /// <summary>X 軸最大值（右邊界）</summary>
        public float MaxX { get; set; }
        
        /// <summary>Y 軸最小值（上邊界）</summary>
        public float MinY { get; set; }
        
        /// <summary>Y 軸最大值（下邊界）</summary>
        public float MaxY { get; set; }

        /// <summary>
        /// 檢查座標是否在安全邊界內
        /// </summary>
        public bool IsWithinBounds(float x, float y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }

        /// <summary>
        /// 檢查座標是否接近邊界（在緩衝區內）
        /// </summary>
        public bool IsNearBoundary(float x, float y, float bufferZone)
        {
            return x <= MinX + bufferZone || x >= MaxX - bufferZone ||
                   y <= MinY + bufferZone || y >= MaxY - bufferZone;
        }

        public override string ToString() => 
            $"X=[{MinX:F1}, {MaxX:F1}], Y=[{MinY:F1}, {MaxY:F1}]";
    }
}
