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

        [Menu(
            "Verify Fills",
            "After typing, read the field back from game memory and retry if it doesn't match."
        )]
        public ToggleNode VerifyFills { get; set; } = new(true);

        [Menu("Max Fill Retries", "How many times to re-click and re-type a field that fails verification")]
        public RangeNode<int> MaxFillRetries { get; set; } = new(3, 1, 6);

        [Menu(
            "Abort If Mouse Moves",
            "Cancel the fill immediately if you move the cursor, preventing keystrokes from landing in the wrong place."
        )]
        public ToggleNode AbortOnMouseMove { get; set; } = new(true);

        [Menu(
            "Park Cursor On Place Order",
            "After a successful fill, move the cursor onto the Place Order button instead of restoring it."
        )]
        public ToggleNode MoveCursorToPlaceOrder { get; set; } = new(true);

        [Menu("Include Stash", "Include currency from the currently visible stash tab")]
        public ToggleNode IncludeStash { get; set; } = new(true);

        [Menu("Show Debug Info", "Display debug information for stock items")]
        public ToggleNode ShowDebugInfo { get; set; } = new(false);
    }
}
