using System.Drawing;
using ArtaleAI.Models.Config;
using OpenCvSharp;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Application.Pipeline.ChannelPick
{
    /// <summary>策略 A：可見格隨機點（遇人換頻主力）。</summary>
    internal sealed class RandomChannelCellSelector : IChannelCellSelector
    {
        public const string StrategyName = "random";

        private readonly Random _rng;

        public RandomChannelCellSelector(Random? rng = null)
        {
            _rng = rng ?? Random.Shared;
        }

        public ChannelPickResult Select(
            Mat? frame,
            Rectangle panelBounds,
            ChannelPickGridSettings grid)
        {
            grid.Clamp();
            int column = _rng.Next(grid.Columns);
            int row = _rng.Next(grid.VisibleRows);
            Rectangle cell = ChannelPickGridGeometry.GetCellBounds(panelBounds, grid, column, row);
            SdPoint click = ChannelPickGridGeometry.GetJitteredCellClick(cell, _rng);
            return new ChannelPickResult(click.X, click.Y, column, row, StrategyName);
        }
    }
}
