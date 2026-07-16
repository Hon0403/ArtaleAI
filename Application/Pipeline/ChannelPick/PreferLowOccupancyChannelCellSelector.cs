using System.Drawing;
using ArtaleAI.Models.Config;
using OpenCvSharp;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Application.Pipeline.ChannelPick
{
    /// <summary>
    /// 策略 B：偏好佔用條偏藍綠（較空）的可見格；偵測失敗時退回隨機。
    /// </summary>
    internal sealed class PreferLowOccupancyChannelCellSelector : IChannelCellSelector
    {
        public const string StrategyName = "preferLowOccupancy";

        private const double CandidateFraction = 0.3;
        private readonly IChannelCellSelector _fallback;
        private readonly Random _rng;

        public PreferLowOccupancyChannelCellSelector(
            IChannelCellSelector? fallback = null,
            Random? rng = null)
        {
            _fallback = fallback ?? new RandomChannelCellSelector(rng);
            _rng = rng ?? Random.Shared;
        }

        public ChannelPickResult Select(
            Mat? frame,
            Rectangle panelBounds,
            ChannelPickGridSettings grid)
        {
            grid.Clamp();
            if (frame == null || frame.Empty())
                return Relabel(_fallback.Select(null, panelBounds, grid));

            var scored = new List<(int Column, int Row, double Score, Rectangle Cell)>();
            for (int row = 0; row < grid.VisibleRows; row++)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    Rectangle cell = ChannelPickGridGeometry.GetCellBounds(panelBounds, grid, col, row);
                    if (!TryScoreOccupancy(frame, cell, out double score))
                        continue;
                    scored.Add((col, row, score, cell));
                }
            }

            if (scored.Count == 0)
                return Relabel(_fallback.Select(frame, panelBounds, grid));

            scored.Sort((a, b) => a.Score.CompareTo(b.Score));
            int keep = Math.Max(1, (int)Math.Ceiling(scored.Count * CandidateFraction));
            var pick = scored[_rng.Next(keep)];
            SdPoint click = ChannelPickGridGeometry.GetJitteredCellClick(pick.Cell, _rng);
            return new ChannelPickResult(click.X, click.Y, pick.Column, pick.Row, StrategyName);
        }

        /// <summary>
        /// 取格子下方佔用條 ROI：藍綠分量相對紅越高 → 分數越低（越空）。
        /// </summary>
        private static bool TryScoreOccupancy(Mat frame, Rectangle cell, out double score)
        {
            score = double.MaxValue;
            int barTop = cell.Y + (int)(cell.Height * 0.55);
            int barHeight = Math.Max(2, (int)(cell.Height * 0.35));
            var bar = new Rectangle(
                cell.X + cell.Width / 8,
                barTop,
                Math.Max(2, cell.Width * 3 / 4),
                barHeight);

            if (bar.Right > frame.Width || bar.Bottom > frame.Height || bar.X < 0 || bar.Y < 0)
                return false;

            using var roi = new Mat(frame, new OpenCvSharp.Rect(bar.X, bar.Y, bar.Width, bar.Height));
            Scalar mean = Cv2.Mean(roi);
            // OpenCV BGR：Val0=B, Val1=G, Val2=R
            double b = mean.Val0;
            double g = mean.Val1;
            double r = mean.Val2;
            double warm = r + 1.0;
            score = warm / (b + g + 1.0);
            return true;
        }

        private static ChannelPickResult Relabel(ChannelPickResult fallback)
            => fallback with { Strategy = StrategyName + "+fallback" };
    }
}
