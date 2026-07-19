using System.Collections.Generic;

namespace ArtaleAI.Models.Config
{
    public class VisionSettings
    {
        public double DefaultThreshold { get; set; } = 0.75;
        public int MaxDetectionResults { get; set; } = 100;
        public string DetectionMode { get; set; } = "Color";

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

        public int MinBarWidth { get; set; } = 10;
        public int MaxBarWidth { get; set; } = 200;
        public int MinBarHeight { get; set; } = 2;
        public int MaxBarHeight { get; set; } = 10;
        public int[] LowerRedHsv { get; set; } = new[] { 0, 100, 100 };
        public int[] UpperRedHsv { get; set; } = new[] { 10, 255, 255 };

        public double MinAspectRatio { get; set; } = 2.0;
        public double MaxAspectRatio { get; set; } = 15.0;

        public int DotOffsetY { get; set; } = -5;
        public int DetectionBoxWidth { get; set; } = 100;
        public int DetectionBoxHeight { get; set; } = 100;
        /// <summary>
        /// 血條可玩區從底部排除的高度佔畫面比例（0~0.9）。用百分比避免不同解析度失真。
        /// </summary>
        public double BloodBarBottomUiPercent { get; set; } = 0.28;
        public int MinBarArea { get; set; } = 20;

        public Dictionary<string, DetectionModeConfig> DetectionModes { get; set; } = new();
        public string DefaultMode { get; set; } = "Color";
        public List<string> DisplayOrder { get; set; } = new();

        public int BloodBarDetectIntervalMs { get; set; } = 100;
        public int MonsterDetectIntervalMs { get; set; } = 100;
        public int CaptureFrameRate { get; set; } = 30;

        public bool UseFixedMinimapPosition { get; set; } = false;
        public int FixedMinimapWidth { get; set; } = 250;
        public int FixedMinimapHeight { get; set; } = 150;
        public string MinimapFrameColorBgr { get; set; } = "255,255,255";
        public int MinMinimapWidth { get; set; } = 100;
        public int MinMinimapHeight { get; set; } = 80;

        /// <summary>
        /// 小地圖搜尋是否限制在百分比 ROI（預設左上角）。
        /// 關閉則掃全畫面；<see cref="UseFixedMinimapPosition"/> 為 true 時仍優先用固定像素框。
        /// </summary>
        public bool UseMinimapSearchRoi { get; set; } = true;

        /// <summary>小地圖搜尋 ROI（相對全畫面寬高比例 0~1，錨點在左上）。</summary>
        public MinimapSearchRoiPercent MinimapSearchRoi { get; set; } = new();

        /// <summary>
        /// 未鎖定時，必須有小地圖自己黃點才搜尋（擋登入／選角畫面誤搜）。
        /// </summary>
        public bool BloodBarRequireMinimapSelf { get; set; } = true;

        /// <summary>血條搜尋時挖空小地圖 ROI，避免「其他玩家」紅條誤判。</summary>
        public bool ExcludeMinimapRoiFromBloodBar { get; set; } = true;

        /// <summary>
        /// 固定外框模式：紅填充定位候選 + 固定寬高外框 + 端點驗證（擋天空／木頭誤判）。
        /// </summary>
        public bool UseBloodBarFixedFrame { get; set; } = true;

        /// <summary>隊伍血條外框固定寬度（像素，對應擷取解析度）。</summary>
        public int BloodBarFrameWidth { get; set; } = 46;

        /// <summary>隊伍血條外框固定高度（像素）。</summary>
        public int BloodBarFrameHeight { get; set; } = 6;

        /// <summary>端點近白最低明度 V（HSV）。</summary>
        public int BloodBarFrameTipMinBgr { get; set; } = 120;

        /// <summary>左右端點最低近白覆蓋率（左右都要達標）。</summary>
        public double BloodBarFrameTipMinSide { get; set; } = 0.12;

        /// <summary>
        /// 外框內部最低嚴格紅覆蓋率。低血時紅條變短，宜 0.10~0.15；
        /// 過高（如 0.35）會在約 1/3 血以下漏檢。
        /// </summary>
        public double BloodBarFrameMinInteriorRed { get; set; } = 0.12;

        /// <summary>紅填充最小寬度（像素）。過高會讓低血漏檢。</summary>
        public int BloodBarFrameMinFillWidth { get; set; } = 5;

        /// <summary>最低接受分數；低於此視為無血條（擋關閉時誤判）。</summary>
        public double BloodBarFrameMinAcceptScore { get; set; } = 0.50;

        /// <summary>
        /// 外框中段近白上限。活動 ICON／圓形徽章中間常有亮邊，真血條中段應偏紅或空。
        /// </summary>
        public double BloodBarFrameMaxInteriorTip { get; set; } = 0.12;

        /// <summary>
        /// 紅填充最小長寬比（擋方形活動 ICON）。
        /// 僅在填充寬度 ≥ 高×此值時套用；更短的低血紅條不套用，改靠高度與端點。
        /// </summary>
        public double BloodBarFrameMinFillAspect { get; set; } = 2.5;

        /// <summary>上一幀外框追蹤半徑（像素）；候選中心在此距離內加分。</summary>
        public int BloodBarTrackRadiusPx { get; set; } = 80;

        /// <summary>上一幀位置加分權重（加到評分上）。</summary>
        public double BloodBarTrackWeight { get; set; } = 0.35;

        /// <summary>多久沒偵測到就清除上一幀記憶（毫秒）。</summary>
        public int BloodBarTrackHoldMs { get; set; } = 2000;
    }

    /// <summary>小地圖搜尋區：以畫面百分比描述（左、上、寬、高）。</summary>
    public class MinimapSearchRoiPercent
    {
        /// <summary>左緣 X，佔畫面寬度比例。</summary>
        public double LeftPercent { get; set; } = 0;

        /// <summary>上緣 Y，佔畫面高度比例。</summary>
        public double TopPercent { get; set; } = 0;

        /// <summary>搜尋區寬度，佔畫面寬度比例。</summary>
        public double WidthPercent { get; set; } = 0.30;

        /// <summary>搜尋區高度，佔畫面高度比例。</summary>
        public double HeightPercent { get; set; } = 0.45;
    }

    public class DetectionModeConfig
    {
        public string DisplayName { get; set; } = "";
    }
}
