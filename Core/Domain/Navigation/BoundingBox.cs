using System;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>軸對齊包圍盒（AABB）。</summary>
    public struct BoundingBox
    {
        public float MinX { get; }
        public float MaxX { get; }
        public float MinY { get; }
        public float MaxY { get; }

        public BoundingBox(float centerX, float centerY, float width, float height)
        {
            float halfWidth = width / 2f;
            float halfHeight = height / 2f;

            MinX = centerX - halfWidth;
            MaxX = centerX + halfWidth;
            MinY = centerY - halfHeight;
            MaxY = centerY + halfHeight;
        }

        /// <summary>判斷 (x,y) 是否落於 AABB 內（含邊界）。</summary>
        public bool Contains(float x, float y)
        {
            return x >= MinX && x <= MaxX &&
                   y >= MinY && y <= MaxY;
        }

        public override string ToString()
        {
            return $"AABB(X:[{MinX:F1}, {MaxX:F1}], Y:[{MinY:F1}, {MaxY:F1}])";
        }
    }
}
