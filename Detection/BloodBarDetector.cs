using ArtaleAI.Config;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 血條檢測器 - 純邏輯，無UI依賴
    /// </summary>
    public static class BloodBarDetector
    {
        /// <summary>
        /// 提取相機區域（排除UI）
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
        /// 創建紅色遮罩
        /// </summary>
        public static Mat CreateRedMask(Mat hsvImage, PartyRedBarSettings config)
        {
            var lowerRed = UtilityHelper.ToOpenCvHsv((config.LowerRedHsv[0], config.LowerRedHsv[1], config.LowerRedHsv[2]));
            var upperRed = UtilityHelper.ToOpenCvHsv((config.UpperRedHsv[0], config.UpperRedHsv[1], config.UpperRedHsv[2]));
            var redMask = new Mat();
            Cv2.InRange(hsvImage, lowerRed, upperRed, redMask);
            return redMask;
        }

        /// <summary>
        /// 在紅色遮罩中找到最佳血條
        /// </summary>
        public static Rectangle? FindBestRedBar(Mat redMask, PartyRedBarSettings config)
        {
            var contours = new Mat[0];
            var hierarchy = new Mat();
            Cv2.FindContours(redMask, out contours, hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var candidates = new List<(Rectangle rect, int area)>();
            foreach (var contour in contours)
            {
                var boundingRect = Cv2.BoundingRect(contour);
                var rect = new Rectangle(boundingRect.X, boundingRect.Y, boundingRect.Width, boundingRect.Height);

                if (IsValidBloodBar(rect, config))
                {
                    candidates.Add((rect, rect.Width * rect.Height));
                }
                contour.Dispose();
            }

            return candidates.OrderByDescending(c => c.area).FirstOrDefault().rect;
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
