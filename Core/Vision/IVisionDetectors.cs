using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace ArtaleAI.Core.Vision
{
    /// <summary>視覺偵測器共同契約（名稱供日誌識別）。</summary>
    public interface IVisionDetector : IDisposable
    {
        /// <summary>偵測器名稱（用於 Log 識別）</summary>
        string Name { get; }
    }

    /// <summary>HSV 紅色血條與衍生怪物／攻擊範圍框。</summary>
    public interface IBloodBarDetector : IVisionDetector
    {
        System.Drawing.Rectangle? DetectBloodBar(
            Mat frameMat,
            System.Drawing.Rectangle? uiExcludeRect,
            out float cameraOffsetY);

        (System.Drawing.Rectangle? BloodBar,
         List<System.Drawing.Rectangle> DetectionBoxes,
         List<System.Drawing.Rectangle> AttackRangeBoxes)
        ProcessBloodBarDetection(Mat frameMat, System.Drawing.Rectangle? uiExcludeRect);
    }

    /// <summary>小地圖框定位與玩家追蹤。</summary>
    public interface IMinimapDetector : IVisionDetector
    {
        System.Drawing.Rectangle? FindMinimapOnScreen(Mat fullFrameMat);

        MinimapTrackingResult? GetMinimapTracking(Mat fullFrameMat, DateTime captureTime);

        /// <summary>最後一幀小地圖複本（呼叫者 Dispose）。</summary>
        Mat? GetLastMinimapMatClone();
    }

    /// <summary>模板匹配、NMS 與模板載入。</summary>
    public interface IMonsterDetector : IVisionDetector
    {
        List<DetectionResult> FindMonsters(
            Mat sourceMat,
            List<Mat> templateMats,
            MonsterDetectionMode detectionMode,
            double threshold,
            string monsterName);

        List<DetectionResult> ApplyNMS(
            List<DetectionResult> results,
            double iouThreshold,
            bool higherIsBetter);

        Task<List<Mat>> LoadMonsterTemplatesAsync(string folderPath, string monsterName);
    }
}
