using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Shared;
using ArtaleAI.Infrastructure.Capture;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ArtaleAI.Vision
{
    /// <summary>整合的遊戲視覺核心。</summary>
    public partial class GameVisionCore
    {
        #region 血條檢測功能群組

        /// <summary>
        /// 檢測隊友血條位置（主要入口方法）
        /// 使用 HSV 色彩空間檢測紅色血條
        /// </summary>
        /// <param name="frameMat">輸入畫面 Mat</param>
        /// <param name="uiExcludeRect">UI 排除區域（可選）</param>
        /// <param name="config">應用程式設定</param>
        /// <param name="cameraOffsetY">相機垂直偏移量（輸出參數）</param>
        /// <returns>血條矩形區域，未檢測到時返回 null</returns>
        public Rectangle? DetectBloodBar(Mat frameMat, Rectangle? uiExcludeRect, AppConfig config, out int cameraOffsetY)
        {
            Mat cameraArea;
            if (uiExcludeRect.HasValue)
            {
                var cameraHeight = uiExcludeRect.Value.Y;
                cameraOffsetY = 0;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }
            else
            {
                var totalHeight = frameMat.Height;
                var uiHeight = config.Vision.UiHeightFromBottom;
                var cameraHeight = Math.Max(totalHeight - uiHeight, totalHeight / 2);
                cameraOffsetY = 0;
                cameraArea = frameMat[new OpenCvSharp.Rect(0, 0, frameMat.Width, cameraHeight)].Clone();
            }

            using (cameraArea)
            {
                using var hsvImage = new Mat();
                Cv2.CvtColor(cameraArea, hsvImage, ColorConversionCodes.BGR2HSV);

                using var redMask = new Mat();
                
                var lower1 = new Scalar(config.Vision.LowerRedHsv[0], config.Vision.LowerRedHsv[1], config.Vision.LowerRedHsv[2]);
                var upper1 = new Scalar(config.Vision.UpperRedHsv[0], config.Vision.UpperRedHsv[1], config.Vision.UpperRedHsv[2]);
                using var mask1 = new Mat();
                Cv2.InRange(hsvImage, lower1, upper1, mask1);

                var lower2 = new Scalar(160, config.Vision.LowerRedHsv[1], config.Vision.LowerRedHsv[2]);
                var upper2 = new Scalar(180, 255, 255);
                using var mask2 = new Mat();
                Cv2.InRange(hsvImage, lower2, upper2, mask2);

                Cv2.BitwiseOr(mask1, mask2, redMask);

                var bestBar = FindBestRedBar(redMask, config);

                return bestBar.HasValue
                    ? new Rectangle(bestBar.Value.X, bestBar.Value.Y + cameraOffsetY,
                                   bestBar.Value.Width, bestBar.Value.Height)
                    : null;
            }
        }

        /// <summary>
        /// 完整的血條檢測處理（一次性計算所有相關資訊）
        /// 檢測血條並同時計算檢測框和攻擊範圍框
        /// </summary>
        /// <param name="frameMat">輸入畫面 Mat</param>
        /// <param name="uiExcludeRect">UI 排除區域（可選）</param>
        /// <returns>包含血條、檢測框列表和攻擊範圍框列表的元組</returns>
        public (Rectangle? BloodBar, List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            ProcessBloodBarDetection(Mat frameMat, Rectangle? uiExcludeRect)
        {
            var config = AppConfig.Instance;
            var bloodBar = DetectBloodBar(frameMat, uiExcludeRect, config, out _);

            if (bloodBar.HasValue)
            {
                var (detectionBoxes, attackRangeBoxes) =
                    CalculateBloodBarRelatedBoxes(bloodBar.Value, config);

                return (bloodBar, detectionBoxes, attackRangeBoxes);
            }

            return (null, new List<Rectangle>(), new List<Rectangle>());
        }


        /// <summary>
        /// 計算血條相關的所有框架（檢測框 + 攻擊範圍框）
        /// 根據血條位置計算怪物檢測區域和角色攻擊範圍
        /// </summary>
        /// <param name="bloodBarRect">血條矩形區域</param>
        /// <param name="config">應用程式設定</param>
        /// <returns>包含檢測框列表和攻擊範圍框列表的元組</returns>
        public (List<Rectangle> DetectionBoxes, List<Rectangle> AttackRangeBoxes)
            CalculateBloodBarRelatedBoxes(Rectangle bloodBarRect, AppConfig config)
        {
            var dotCenterX = bloodBarRect.X + bloodBarRect.Width / 2;
            var dotCenterY = bloodBarRect.Y + bloodBarRect.Height + config.Vision.DotOffsetY;

            var detectionBox = new Rectangle(
                dotCenterX - config.Vision.DetectionBoxWidth / 2,
                dotCenterY - config.Vision.DetectionBoxHeight / 2,
                config.Vision.DetectionBoxWidth,
                config.Vision.DetectionBoxHeight
            );

            var playerCenterX = bloodBarRect.X + bloodBarRect.Width / 2 + config.Appearance.AttackRange.OffsetX;
            var playerCenterY = bloodBarRect.Y + bloodBarRect.Height + config.Appearance.AttackRange.OffsetY;

            var attackRangeBox = new Rectangle(
                playerCenterX - config.Appearance.AttackRange.Width / 2,
                playerCenterY - config.Appearance.AttackRange.Height / 2,
                config.Appearance.AttackRange.Width,
                config.Appearance.AttackRange.Height
            );

            return (
                new List<Rectangle> { detectionBox },
                new List<Rectangle> { attackRangeBox }
            );
        }

        #endregion
        #region 私有輔助方法 - 血條相關

        /// <summary>
        /// 從紅色遮罩中找出最佳的血條候選
        /// 使用輪廓檢測和多重條件篩選找出最符合血條特徵的矩形
        /// </summary>
        /// <param name="redMask">紅色二值化遮罩</param>
        /// <param name="config">應用程式設定（包含血條尺寸限制）</param>
        /// <returns>最佳血條矩形，未找到時返回 null</returns>
        private Rectangle? FindBestRedBar(Mat redMask, AppConfig config)
        {
            Mat? hierarchy = null;
            Mat[]? contours = null;

            try
            {
                hierarchy = new Mat();
                Cv2.FindContours(redMask, out contours, hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var candidates = new List<(Rectangle rect, int area)>();

                for (int i = 0; i < contours.Length; i++)
                {
                    var contour = contours[i];
                    if (contour?.Empty() != false) continue;

                    try
                    {
                        var boundingRect = Cv2.BoundingRect(contour);
                        var rect = new Rectangle(boundingRect.X, boundingRect.Y,
                            boundingRect.Width, boundingRect.Height);

                        var width = rect.Width;
                        var height = rect.Height;
                        var area = width * height;

                        if (width >= config.Vision.MinBarWidth &&
                            width <= config.Vision.MaxBarWidth &&
                            height >= config.Vision.MinBarHeight &&
                            height <= config.Vision.MaxBarHeight &&
                            area >= config.Vision.MinBarArea)
                        {
                            candidates.Add((rect, area));
                        }
                    }
                    finally
                    {
                        contour?.Dispose();
                    }
                }

                if (candidates.Count == 0) return null;

                var bestCandidate = candidates.OrderByDescending(c => c.area).First();
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

        #endregion
    }
}
