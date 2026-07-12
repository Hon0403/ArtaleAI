using ArtaleAI.Models.Config;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using SdRect = System.Drawing.Rectangle;

namespace ArtaleAI.Vision.Detectors
{
    /// <summary>自底部 UI 色條自動推算 HP／MP ROI，免手調百分比。</summary>
    internal static class PlayerVitalsAutoLayout
    {
        internal static bool TryDetectAnchors(
            Mat frameMat,
            PlayerVitalsSettings settings,
            out BarRoiPercentAnchor hpAnchor,
            out BarRoiPercentAnchor mpAnchor)
        {
            hpAnchor = new BarRoiPercentAnchor();
            mpAnchor = new BarRoiPercentAnchor();

            if (frameMat == null || frameMat.Empty() || settings == null)
                return false;

            int fw = frameMat.Width;
            int fh = frameMat.Height;
            int bandTop = PercentToPixels(settings.UiBandTopPercent, fh, minPixels: 0);
            bandTop = Math.Clamp(bandTop, 0, Math.Max(0, fh - 1));
            int bandHeight = fh - bandTop;
            if (bandHeight < 8 || fw < 100)
                return false;

            var stripRect = new Rect(0, bandTop, fw, bandHeight);
            using var strip = frameMat[stripRect].Clone();
            using var hsv = new Mat();
            Cv2.CvtColor(strip, hsv, ColorConversionCodes.BGR2HSV);

            int rowY0 = bandHeight / 4;
            int rowY1 = Math.Max(rowY0 + 1, bandHeight * 3 / 4);

            using var hpMask = BuildHpMask(strip, hsv, settings);
            using var mpMask = BuildMpMask(strip, hsv, settings);
            ExcludeAnnouncementPink(strip, hpMask);
            ExcludeAnnouncementPink(strip, mpMask);

            int minWidth = Math.Max(40, (int)Math.Round(fw * 0.07));
            int maxWidth = Math.Max(minWidth + 1, (int)Math.Round(fw * 0.22));
            int minLeft = (int)Math.Round(fw * 0.18);

            var hpRuns = FindHorizontalRuns(hpMask, rowY0, rowY1, minWidth, maxWidth);
            if (hpRuns.Count == 0)
                return false;

            var hpRun = hpRuns[0];
            foreach (var run in hpRuns)
            {
                if (run.Start >= minLeft)
                {
                    hpRun = run;
                    break;
                }
            }

            if (hpRun.Start < minLeft)
                return false;

            using var mpSearch = mpMask.Clone();
            ZeroColumns(mpSearch, 0, hpRun.End + 3);

            var mpRuns = FindHorizontalRuns(mpSearch, rowY0, rowY1, minWidth, maxWidth);
            if (mpRuns.Count == 0)
                return false;

            var mpRun = mpRuns[0];
            int gap = Math.Clamp(mpRun.Start - hpRun.End - 1, 2, 24);
            int barWidth = mpRun.Start - gap - hpRun.Start;
            if (barWidth < minWidth || barWidth > maxWidth)
                barWidth = Math.Clamp(mpRun.End - mpRun.Start + 1, minWidth, maxWidth);

            if (!TryGetVerticalBounds(hpMask, hpRun.Start, hpRun.End, rowY0, rowY1, out int topY, out int barHeight)
                && !TryGetVerticalBounds(mpMask, mpRun.Start, mpRun.End, rowY0, rowY1, out topY, out barHeight))
            {
                topY = rowY0;
                barHeight = Math.Max(4, rowY1 - rowY0);
            }

            var hpRect = new SdRect(hpRun.Start, bandTop + topY, barWidth, barHeight);
            var mpRect = new SdRect(mpRun.Start, bandTop + topY, barWidth, barHeight);

            if (!IsInsideFrame(fw, fh, hpRect) || !IsInsideFrame(fw, fh, mpRect))
                return false;

            hpAnchor = RectToAnchor(hpRect, fw, fh);
            mpAnchor = RectToAnchor(mpRect, fw, fh);
            return true;
        }

        internal static BarRoiPercentAnchor RectToAnchor(SdRect rect, int frameWidth, int frameHeight)
        {
            return new BarRoiPercentAnchor
            {
                LeftPercent = rect.X / (double)frameWidth,
                WidthPercent = rect.Width / (double)frameWidth,
                BottomPercent = (frameHeight - rect.Bottom) / (double)frameHeight,
                HeightPercent = rect.Height / (double)frameHeight
            };
        }

        private static Mat BuildHpMask(Mat strip, Mat hsv, PlayerVitalsSettings settings)
        {
            using var hsvMask = BuildColorMask(hsv, settings.LowerRedHsv, settings.UpperRedHsv, isRed: true);
            using var bgrMask = BuildBgrMask(strip, isRed: true);
            var mask = new Mat();
            Cv2.BitwiseOr(hsvMask, bgrMask, mask);
            return mask;
        }

        private static Mat BuildMpMask(Mat strip, Mat hsv, PlayerVitalsSettings settings)
        {
            using var hsvMask = BuildColorMask(hsv, settings.LowerBlueHsv, settings.UpperBlueHsv, isRed: false);
            using var bgrMask = BuildBgrMask(strip, isRed: false);
            var mask = new Mat();
            Cv2.BitwiseOr(hsvMask, bgrMask, mask);
            return mask;
        }

        private static Mat BuildBgrMask(Mat bgr, bool isRed)
        {
            var mask = new Mat();
            if (isRed)
                Cv2.InRange(bgr, new Scalar(0, 0, 130), new Scalar(115, 115, 255), mask);
            else
                Cv2.InRange(bgr, new Scalar(110, 0, 0), new Scalar(185, 185, 210), mask);
            return mask;
        }

        private static Mat BuildColorMask(Mat hsv, int[] lowerHsv, int[] upperHsv, bool isRed)
        {
            var lower = new Scalar(lowerHsv[0], lowerHsv[1], lowerHsv[2]);
            var upper = new Scalar(upperHsv[0], upperHsv[1], upperHsv[2]);
            var mask = new Mat();
            if (isRed)
            {
                using var mask1 = new Mat();
                using var mask2 = new Mat();
                Cv2.InRange(hsv, lower, upper, mask1);
                Cv2.InRange(hsv, new Scalar(160, lowerHsv[1], lowerHsv[2]), new Scalar(180, 255, 255), mask2);
                Cv2.BitwiseOr(mask1, mask2, mask);
            }
            else
            {
                Cv2.InRange(hsv, lower, upper, mask);
            }

            return mask;
        }

        private static void ExcludeAnnouncementPink(Mat bgr, Mat mask)
        {
            using var pink = new Mat();
            using var notPink = new Mat();
            Cv2.InRange(bgr, new Scalar(130, 100, 170), new Scalar(255, 200, 255), pink);
            Cv2.BitwiseNot(pink, notPink);
            Cv2.BitwiseAnd(mask, notPink, mask);
        }

        private static void ZeroColumns(Mat mask, int fromX, int toXExclusive)
        {
            if (toXExclusive <= fromX)
                return;

            fromX = Math.Clamp(fromX, 0, mask.Width);
            toXExclusive = Math.Clamp(toXExclusive, 0, mask.Width);
            if (toXExclusive <= fromX)
                return;

            mask.ColRange(fromX, toXExclusive).SetTo(Scalar.All(0));
        }

        private static List<HorizontalRun> FindHorizontalRuns(
            Mat mask,
            int rowY0,
            int rowY1,
            int minWidth,
            int maxWidth)
        {
            var projection = new byte[mask.Width];
            for (int x = 0; x < mask.Width; x++)
            {
                for (int y = rowY0; y <= rowY1 && y < mask.Height; y++)
                {
                    if (mask.At<byte>(y, x) > 0)
                    {
                        projection[x] = 255;
                        break;
                    }
                }
            }

            var runs = new List<HorizontalRun>();
            int i = 0;
            while (i < projection.Length)
            {
                if (projection[i] == 0)
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < projection.Length && projection[i] > 0)
                    i++;

                int width = i - start;
                if (width >= minWidth && width <= maxWidth)
                    runs.Add(new HorizontalRun(start, i - 1));
            }

            return runs;
        }

        private static bool TryGetVerticalBounds(
            Mat mask,
            int x0,
            int x1,
            int rowY0,
            int rowY1,
            out int topY,
            out int height)
        {
            topY = 0;
            height = 0;
            int minY = int.MaxValue;
            int maxY = -1;
            x0 = Math.Clamp(x0, 0, mask.Width - 1);
            x1 = Math.Clamp(x1, 0, mask.Width - 1);

            for (int y = rowY0; y <= rowY1 && y < mask.Height; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (mask.At<byte>(y, x) == 0)
                        continue;

                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxY < minY)
                return false;

            topY = minY;
            height = Math.Max(3, maxY - minY + 1);
            return true;
        }

        private static bool IsInsideFrame(int frameWidth, int frameHeight, SdRect rect)
        {
            return rect.X >= 0
                   && rect.Y >= 0
                   && rect.Width > 0
                   && rect.Height > 0
                   && rect.Right <= frameWidth
                   && rect.Bottom <= frameHeight;
        }

        private static int PercentToPixels(double percent, int frameSize, int minPixels = 1)
        {
            if (frameSize <= 0)
                return minPixels;

            double clamped = Math.Clamp(percent, 0, 1);
            return Math.Max(minPixels, (int)Math.Round(frameSize * clamped));
        }

        private readonly struct HorizontalRun(int start, int end)
        {
            public int Start { get; } = start;
            public int End { get; } = end;
        }
    }
}
