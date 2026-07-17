using ArtaleAI.Models.Map;
using ArtaleAI.Shared;

namespace ArtaleAI.Application.MapEditor
{
    /// <summary>
    /// 將舊版 SafeZones 線段遷移為帶 IsSafeZone 旗標的短路徑平台。
    /// </summary>
    public static class MapSafeZoneMigration
    {
        /// <summary>
        /// 若存在舊 SafeZones，各轉成兩點平台（兩端 IsSafeZone=true）並清空 SafeZones。
        /// </summary>
        /// <returns>遷移的線段數；0 表示無需遷移。</returns>
        public static int MigrateLegacySafeZones(MapData mapData)
        {
            if (mapData.SafeZones == null || mapData.SafeZones.Count == 0)
                return 0;

            mapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
            int migrated = 0;
            int nextId = 0;

            foreach (var zone in mapData.SafeZones)
            {
                if (zone == null || zone.Length < 4)
                    continue;

                string id = AllocatePlatformId(mapData.PolylinePlatforms, ref nextId);
                mapData.PolylinePlatforms.Add(new PolylinePlatformData
                {
                    Id = id,
                    Points = new List<PlatformPointData>
                    {
                        new() { X = zone[0], Y = zone[1], IsSafeZone = true },
                        new() { X = zone[2], Y = zone[3], IsSafeZone = true }
                    }
                });
                migrated++;
            }

            mapData.SafeZones = null;
            if (migrated > 0)
                Logger.Info($"[地圖遷移] 已將 {migrated} 條舊安全區線段轉為路徑標記（IsSafeZone）");

            return migrated;
        }

        private static string AllocatePlatformId(List<PolylinePlatformData> platforms, ref int hint)
        {
            var used = new HashSet<string>(
                platforms.Select(p => p.Id),
                StringComparer.Ordinal);

            while (true)
            {
                string id = $"plat_{hint++}";
                if (used.Add(id))
                    return id;
            }
        }
    }
}
