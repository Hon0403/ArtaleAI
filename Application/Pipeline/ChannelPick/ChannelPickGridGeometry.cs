using System.Drawing;
using ArtaleAI.Models.Config;

namespace ArtaleAI.Application.Pipeline.ChannelPick
{
    /// <summary>由面板錨點矩形換算可見頻道格的客戶區座標。</summary>
    internal static class ChannelPickGridGeometry
    {
        public static Rectangle GetGridBounds(Rectangle panelBounds, ChannelPickGridSettings grid)
        {
            grid.Clamp();
            int left = panelBounds.X + RatioToPx(panelBounds.Width, grid.LeftRatio);
            int top = panelBounds.Y + RatioToPx(panelBounds.Height, grid.TopRatio);
            int right = panelBounds.X + RatioToPx(panelBounds.Width, grid.RightRatio);
            int bottom = panelBounds.Y + RatioToPx(panelBounds.Height, grid.BottomRatio);
            return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
        }

        public static Rectangle GetCellBounds(Rectangle panelBounds, ChannelPickGridSettings grid, int column, int row)
        {
            grid.Clamp();
            Rectangle gridBounds = GetGridBounds(panelBounds, grid);
            int cellWidth = Math.Max(1, gridBounds.Width / grid.Columns);
            int cellHeight = Math.Max(1, gridBounds.Height / grid.VisibleRows);
            int x = gridBounds.X + column * cellWidth;
            int y = gridBounds.Y + row * cellHeight;
            return new Rectangle(x, y, cellWidth, cellHeight);
        }

        public static Point GetJitteredCellClick(Rectangle cell, Random rng)
        {
            int insetX = Math.Max(1, cell.Width / 5);
            int insetY = Math.Max(1, cell.Height / 5);
            int spanX = Math.Max(1, cell.Width - insetX * 2);
            int spanY = Math.Max(1, cell.Height - insetY * 2);
            return new Point(
                cell.X + insetX + rng.Next(spanX),
                cell.Y + insetY + rng.Next(spanY));
        }

        private static int RatioToPx(int length, double ratio)
            => (int)Math.Round(length * ratio);
    }
}
