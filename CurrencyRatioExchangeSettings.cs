using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace CurrencyRatioExchange
{
    public class CurrencyRatioExchangeSettings : ISettings
    {
        public ToggleNode Enable { get; set; } = new(true);

        [Menu("Show Calculator", "Display the ratio calculator overlay")]
        public ToggleNode ShowCalculator { get; set; } = new(true);

        [Menu("Click X Offset", "Horizontal offset for input field clicks")]
        public RangeNode<int> ClickXOffset { get; set; } = new(0, -100, 100);

        [Menu("Click Y Offset", "Vertical offset for input field clicks")]
        public RangeNode<int> ClickYOffset { get; set; } = new(0, -100, 100);

        [Menu("Include Stash", "Include currency from the currently visible stash tab")]
        public ToggleNode IncludeStash { get; set; } = new(true);

        [Menu("Show Debug Info", "Display debug information for stock items")]
        public ToggleNode ShowDebugInfo { get; set; } = new(false);
    }
}
