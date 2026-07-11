using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ArtaleAI.Models.Map;

namespace ArtaleAI.Domain.Navigation
{
    /// <summary>平台折線幾何索引；由 MapData.PolylinePlatforms 建立。</summary>
    public sealed class PlatformGeometryIndex
    {
        public static PlatformGeometryIndex Empty { get; } = new PlatformGeometryIndex();

        private readonly Dictionary<string, IReadOnlyList<PointF>> _polylines = new(StringComparer.Ordinal);

        public static PlatformGeometryIndex FromMapData(MapData mapData)
        {
            var index = new PlatformGeometryIndex();
            if (mapData.PolylinePlatforms == null)
                return index;

            foreach (var plat in mapData.PolylinePlatforms)
            {
                if (plat.Points == null || plat.Points.Count < 2)
                    continue;

                index._polylines[plat.Id] = plat.Points
                    .Select(p => new PointF(p.X, p.Y))
                    .ToList();
            }

            return index;
        }

        /// <summary>在平台折線上依 X 插值可站 Y；X 超出段範圍時 fallback 最近段並標記 extrapolated。</summary>
        public bool TryProjectStandY(string platformId, float x, out float y, out bool extrapolated)
        {
            y = 0f;
            extrapolated = false;

            if (!_polylines.TryGetValue(platformId, out var pts) || pts.Count < 2)
                return false;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                float minX = Math.Min(a.X, b.X);
                float maxX = Math.Max(a.X, b.X);

                if (x < minX - 0.01f || x > maxX + 0.01f)
                    continue;

                float abx = b.X - a.X;
                float aby = b.Y - a.Y;
                float t = Math.Abs(abx) < 0.001f ? 0.5f : (x - a.X) / abx;
                t = Math.Max(0f, Math.Min(1f, t));
                y = a.Y + t * aby;
                return true;
            }

            float bestDist = float.MaxValue;
            bool found = false;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                float abx = b.X - a.X;
                float aby = b.Y - a.Y;
                if (Math.Abs(abx) < 0.001f && Math.Abs(aby) < 0.001f)
                    continue;

                float t = Math.Abs(abx) >= 0.001f
                    ? (x - a.X) / abx
                    : (x - a.X) / (aby != 0 ? aby : 0.001f);
                t = Math.Max(0f, Math.Min(1f, t));

                float projX = a.X + t * abx;
                float projY = a.Y + t * aby;
                float dist = Math.Abs(x - projX);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    y = projY;
                    found = true;
                    extrapolated = true;
                }
            }

            return found;
        }
    }
}
