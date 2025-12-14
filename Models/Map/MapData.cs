using System.Collections.Generic;

namespace ArtaleAI.Models.Map
{
    /// <summary>
    /// 地圖資料
    /// 儲存地圖上的所有路徑點、區域標記等資訊
    /// </summary>
    public class MapData
    {
        /// <summary>路徑點列表（用於路徑規劃）
        /// 格式：[x, y] 或 [x, y, actionCode]（向後兼容）
        /// actionCode: 0=None, 1=Left, 2=Right, 3=Up, 4=Down, 
        ///             5=LeftJump, 6=RightJump, 7=DownJump, 8=Jump,
        ///             9=LeftTeleport, 10=RightTeleport, 11=UpTeleport, 12=DownTeleport, 13=Goal
        /// </summary>
        public List<float[]> WaypointPaths { get; set; } = new();
        
        /// <summary>安全區域列表</summary>
        public List<float[]> SafeZones { get; set; } = new();
        
        /// <summary>繩索位置列表</summary>
        public List<float[]> Ropes { get; set; } = new();
        
        /// <summary>限制區域列表（禁止進入的區域）</summary>
        public List<float[]> RestrictedZones { get; set; } = new();
    }
}
