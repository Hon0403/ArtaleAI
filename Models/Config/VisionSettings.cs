using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace ArtaleAI.Models.Config
{
    public class VisionSettings
    {
        public double DefaultThreshold { get; set; } = 0.75;
        public int MaxDetectionResults { get; set; } = 100;
        public string DetectionMode { get; set; } = "Default";
        public double NmsIouThreshold { get; set; } = 0.45;

        /// <summary>
        /// KenYu ContourOnly：SqDiff 最大允許值（res &lt;= 此值才算命中）。數值越低越嚴。
        /// 對齊 MapleStoryAutoLevelUp <c>monster_detect.diff_thres</c> 預設 0.8。
        /// </summary>
        public double ContourDiffThreshold { get; set; } = 0.8;

        /// <summary>
        /// 怪血條關聯過濾（預設關閉）。
        /// Artale 怪血條多半要先攻擊才出現，不能當「發現怪物」的條件，否則未交戰怪會全滅。
        /// 僅適合作為已交戰目標的追蹤補強，不是主偵測過濾。
        /// </summary>
        public bool MonsterHpBarFilterEnabled { get; set; } = false;

        /// <summary>怪血條與怪物頂部的最大垂直間距（像素）。</summary>
        public int MonsterHpBarMaxGapPx { get; set; } = 36;

        /// <summary>KenYu ContourOnly 高斯模糊核（奇數）。對齊 <c>monster_detect.contour_blur</c>。</summary>
        public int ContourBlur { get; set; } = 5;

        public double PlayerThreshold { get; set; } = 0.7;
        public string PlayerMarker { get; set; } = "player.png";
        public string OtherPlayers { get; set; } = "other_players.png";

        public int MinBarWidth { get; set; } = 10;
        public int MaxBarWidth { get; set; } = 200;
        public int MinBarHeight { get; set; } = 2;
        public int MaxBarHeight { get; set; } = 10;
        public double MinFillRate { get; set; } = 0.1;
        public int[] LowerRedHsv { get; set; } = new[] { 0, 100, 100 };
        public int[] UpperRedHsv { get; set; } = new[] { 10, 255, 255 };

        public int SmallBarWidthLimit { get; set; } = 50;
        public int MediumBarWidthLimit { get; set; } = 100;
        public double MinAspectRatio { get; set; } = 2.0;
        public double MaxAspectRatio { get; set; } = 15.0;

        public int DotOffsetY { get; set; } = -5;
        public int DetectionBoxWidth { get; set; } = 100;
        public int DetectionBoxHeight { get; set; } = 100;
        public int UiHeightFromBottom { get; set; } = 150;
        public int MinBarArea { get; set; } = 20;

        public double DynamicFillRateSmall { get; set; } = 0.8;
        public double DynamicFillRateMedium { get; set; } = 0.6;
        public int PlayerOffsetY { get; set; } = 20;

        public Dictionary<string, DetectionModeConfig> DetectionModes { get; set; } = new();
        public string DefaultMode { get; set; } = "Normal";
        public List<string> DisplayOrder { get; set; } = new();

        public int BloodBarDetectIntervalMs { get; set; } = 100;
        public int MonsterDetectIntervalMs { get; set; } = 100;
        public int CaptureFrameRate { get; set; } = 30;

        [YamlIgnore]
        public float VisionFps => CaptureFrameRate > 0 ? (float)CaptureFrameRate : 15.0f;

        public double CornerThreshold { get; set; } = 0.8;
        public string TopLeft { get; set; } = "tl.png";
        public string TopRight { get; set; } = "tr.png";
        public string BottomLeft { get; set; } = "bl.png";
        public string BottomRight { get; set; } = "br.png";

        public bool UseFixedMinimapPosition { get; set; } = false;
        public int FixedMinimapWidth { get; set; } = 250;
        public int FixedMinimapHeight { get; set; } = 150;
        public string MinimapFrameColorBgr { get; set; } = "255,255,255";
        public int MinMinimapWidth { get; set; } = 100;
        public int MinMinimapHeight { get; set; } = 80;
    }

    public class DetectionModeConfig
    {
        public string DisplayName { get; set; } = "";
        public string Occlusion { get; set; } = "None";
    }
}
