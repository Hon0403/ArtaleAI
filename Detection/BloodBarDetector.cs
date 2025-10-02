using ArtaleAI.Config;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 血條檢測器 - 記憶體優化版本，整合 ResourceManager
    /// </summary>
    public static class BloodBarDetector
    {
        /// <summary>
        /// 安全處理血條檢測 - 完整的記憶體管理流程
        /// </summary>
        public static TResult ProcessBloodBarDetection<TResult>(
            Mat frameMat,
            Rectangle? uiExcludeRect,
            PartyRedBarSettings config,
            Func<Mat, Mat, Rectangle?, int, TResult> processor)
        {
            using var cameraArea = ExtractCameraArea(frameMat, uiExcludeRect, config, out int offsetY);
            using var hsvImage = OpenCvProcessor.ConvertToHSV(cameraArea);
            using var redMask = CreateRedMask(hsvImage, config);

            var bestBar = FindBestRedBar(redMask, config);
            return processor(cameraArea, redMask, bestBar, offsetY);
        }

        /// <summary>
        /// 直接返回血條位置
        /// </summary>
        public static Rectangle? DetectBloodBar(
            Mat frameMat,
            Rectangle? uiExcludeRect,
            PartyRedBarSettings config,
            out int cameraOffsetY)
        {
            using var cameraArea = ExtractCameraArea(frameMat, uiExcludeRect, config, out cameraOffsetY);
            using var hsvImage = OpenCvProcessor.ConvertToHSV(cameraArea);
            using var redMask = CreateRedMask(hsvImage, config);

            var bestBar = FindBestRedBar(redMask, config);
            return bestBar.HasValue
                ? ToScreenCoordinates(bestBar.Value, cameraOffsetY)
                : null;
        }


        /// <summary>
        /// 提取相機區域（排除UI）- 記憶體優化版
        /// </summary>
        public static Mat ExtractCameraArea(Mat frameMat, Rectangle? uiExcludeRect, PartyRedBarSettings config, out int offsetY)
        {
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                offsetY = 0;
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                var totalHeight = frameMat.Height;
                var uiHeight = config.UiHeightFromBottom;
                var cameraHeight = Math.Max(totalHeight - uiHeight, totalHeight / 2);
                offsetY = 0;
                return frameMat[new Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
        }

        /// <summary>
        /// 創建紅色遮罩 - 記憶體優化版
        /// </summary>
        public static Mat CreateRedMask(Mat hsvImage, PartyRedBarSettings config)
        {
            var lowerRed = OpenCvProcessor.ToOpenCvHsv(config.LowerRedHsv[0], config.LowerRedHsv[1], config.LowerRedHsv[2]);
            var upperRed = OpenCvProcessor.ToOpenCvHsv(config.UpperRedHsv[0], config.UpperRedHsv[1], config.UpperRedHsv[2]);

            var redMask = new Mat();
            Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);
            return redMask;
        }

        /// <summary>
        /// 在紅色遮罩中找到最佳血條 - 記憶體優化版
        /// </summary>
        public static Rectangle? FindBestRedBar(Mat redMask, PartyRedBarSettings config)
        {
            Mat hierarchy = null;
            Mat[] contours = null;

            try
            {
                hierarchy = new Mat();
                Cv2.FindContours(redMask, out contours, hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var candidates = new List<(Rectangle rect, int area)>();

                for (int i = 0; i < contours.Length; i++)
                {
                    var contour = contours[i];
                    if (contour == null) continue;

                    try
                    {
                        var boundingRect = Cv2.BoundingRect(contour);
                        var rect = new Rectangle(boundingRect.X, boundingRect.Y, boundingRect.Width, boundingRect.Height);

                        if (IsValidBloodBar(rect, config))
                        {
                            candidates.Add((rect, rect.Width * rect.Height));
                        }
                    }
                    finally
                    {
                        contour?.Dispose();
                    }
                }

                if (candidates.Count == 0)
                    return null;

                var bestCandidate = candidates[0];
                for (int i = 1; i < candidates.Count; i++)
                {
                    if (candidates[i].area > bestCandidate.area)
                        bestCandidate = candidates[i];
                }

                return bestCandidate.rect;
            }
            finally
            {
                hierarchy?.Dispose();

                if (contours != null)
                {
                    foreach (var contour in contours)
                    {
                        contour?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 驗證血條是否符合尺寸要求
        /// </summary>
        private static bool IsValidBloodBar(Rectangle rect, PartyRedBarSettings config)
        {
            return rect.Width >= config.MinBarWidth && rect.Width <= config.MaxBarWidth &&
                   rect.Height >= config.MinBarHeight && rect.Height <= config.MaxBarHeight &&
                   (rect.Width * rect.Height) >= config.MinBarArea;
        }

        /// <summary>
        /// 轉換為螢幕座標
        /// </summary>
        public static Rectangle ToScreenCoordinates(Rectangle rect, int cameraOffsetY)
        {
            return new Rectangle(rect.X, rect.Y + cameraOffsetY, rect.Width, rect.Height);
        }

        /// <summary>
        /// 計算檢測框位置
        /// </summary>
        public static List<Rectangle> CalculateDetectionBoxes(Rectangle bloodBarRect, PartyRedBarSettings config)
        {
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + config.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - config.DetectionBoxWidth / 2,
                dotCenterY - config.DetectionBoxHeight / 2,
                config.DetectionBoxWidth,
                config.DetectionBoxHeight);

            return new List<Rectangle> { detectionBox };
        }

        /// <summary>
        /// 計算攻擊範圍框
        /// </summary>
        public static List<Rectangle> CalculateAttackRangeBoxes(Rectangle bloodBarRect, AttackRangeSettings config)
        {
            var playerCenterX = bloodBarRect.X + bloodBarRect.Width / 2 + config.OffsetX;
            var playerCenterY = bloodBarRect.Y + bloodBarRect.Height + config.OffsetY;

            var attackRangeBox = new Rectangle(
                playerCenterX - config.Width / 2,
                playerCenterY - config.Height / 2,
                config.Width,
                config.Height);

            return new List<Rectangle> { attackRangeBox };
        }
    }
}
