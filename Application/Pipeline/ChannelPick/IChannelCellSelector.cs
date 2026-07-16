using System.Drawing;
using ArtaleAI.Models.Config;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline.ChannelPick
{
    internal readonly record struct ChannelPickResult(
        int ClientX,
        int ClientY,
        int Column,
        int Row,
        string Strategy);

    /// <summary>在已對齊的頻道面板內挑選要點的格。</summary>
    internal interface IChannelCellSelector
    {
        ChannelPickResult Select(
            Mat? frame,
            Rectangle panelBounds,
            ChannelPickGridSettings grid);
    }
}
