using System;
using System.Collections.Generic;
using System.Drawing;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Models.Minimap
{
    /// <summary>
    /// 小地圖追蹤結果記錄
    /// 包含玩家位置、其他玩家位置和小地圖邊界資訊
    /// </summary>
    public record MinimapTrackingResult(
        SdPointF? PlayerPosition,
        List<SdPointF> OtherPlayers,
        DateTime Timestamp,
        double Confidence
    )
    {
        /// <summary>小地圖在螢幕上的邊界區域</summary>
        public Rectangle? MinimapBounds { get; init; }
    }
}
