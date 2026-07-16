namespace ArtaleAI.Application.Pipeline.ChannelPick
{
    internal static class ChannelCellSelectorFactory
    {
        public static IChannelCellSelector Create(string? strategy)
        {
            if (string.Equals(
                    strategy?.Trim(),
                    PreferLowOccupancyChannelCellSelector.StrategyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new PreferLowOccupancyChannelCellSelector();
            }

            return new RandomChannelCellSelector();
        }
    }
}
