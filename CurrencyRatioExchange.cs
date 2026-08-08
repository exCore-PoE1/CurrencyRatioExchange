using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using CurrencyRatioExchange.Utils;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ImGuiNET;

namespace CurrencyRatioExchange
{
    public class CurrencyRatioExchange : BaseSettingsPlugin<CurrencyRatioExchangeSettings>
    {
        private string _amountInput = "";
        private string _ratioInput = "";
        private int _calculatedWant = 0;
        private int _calculatedHave = 0;
        private string _errorMessage = "";
        private bool _hasValidResult = false;
        private volatile bool _isProcessing = false;
        private bool _calculateExactWantedAmount = false;

        // Fill status feedback (written from the fill task, read on the render thread)
        private volatile string _statusMessage = "";
        private volatile bool _statusOk = true;

        // For quick-fill from competing trades
        private int _quickFillWant = 0;
        private int _quickFillHave = 0;

        private const int ClickFocusDelayMs = 100;
        private const int ClearBackspaces = 10;
        private const int VerifyTimeoutMs = 600;
        private const int VerifyPollMs = 30;
        private const int CursorDriftThresholdSq = 100;
        private const string PlaceOrderLabelText = "place order";

        public override bool Initialise()
        {
            try
            {
                LogMessage("Currency Ratio Exchange initialized successfully!");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize CurrencyRatioExchange: {ex.Message}");
                return false;
            }
        }

        public override void Render()
        {
            if (!Settings.Enable.Value)
                return;

            var currencyPanel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
            var currencyPicker = GameController
                .IngameState
                ?.IngameUi
                ?.CurrencyExchangePanel
                ?.CurrencyPicker;

            if (currencyPanel == null || !currencyPanel.IsVisible || currencyPicker.IsVisible)
                return;

            if (!Settings.ShowCalculator.Value)
                return;

            // Position calculator overlay near the currency exchange panel
            var panelRect = currencyPanel.GetClientRectCache;
            ImGui.SetNextWindowPos(
                new Vector2(panelRect.X + panelRect.Width + 10, panelRect.Y),
                ImGuiCond.Always
            );
            ImGui.SetNextWindowSizeConstraints(new Vector2(460, 0), new Vector2(600, 2000));

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));

            if (
                ImGui.Begin(
                    "Currency Ratio Calculator",
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize
                )
            )
            {
                ImGui.Text("Calculation Mode:");
                if (ImGui.RadioButton("Max from owned amount", !_calculateExactWantedAmount))
                {
                    _calculateExactWantedAmount = false;
                    Calculate();
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("Buy exact amount", _calculateExactWantedAmount))
                {
                    _calculateExactWantedAmount = true;
                    Calculate();
                }

                // Amount Input
                ImGui.Text(
                    _calculateExactWantedAmount ? "Amount to Buy (Want):" : "Currency Amount:"
                );
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
                if (ImGui.InputText("##amount", ref _amountInput, 32))
                {
                    Calculate();
                }
                ImGui.SameLine();
                int availableForFill = GetOfferedCurrencyAmount();
                if (!_calculateExactWantedAmount && availableForFill > 0)
                {
                    if (ImGui.Button("Fill##fillAmount", new Vector2(42, 0)))
                    {
                        _amountInput = availableForFill.ToString();
                        Calculate();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Fill with available amount: {availableForFill}");
                    }
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Fill##fillAmount", new Vector2(42, 0));
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip(
                            _calculateExactWantedAmount
                                ? "Fill is only available in max amount mode"
                                : "Select an offered currency first"
                        );
                    }
                }

                // Ratio Input (Want:Have format)
                ImGui.Text("Ratio (Want:Have, e.g., 1:3 or 2:5):");
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
                if (ImGui.InputText("##ratio", ref _ratioInput, 64))
                {
                    Calculate();
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear", new Vector2(42, 0)))
                {
                    _amountInput = "";
                    _ratioInput = "";
                    _hasValidResult = false;
                    _errorMessage = "";
                    _calculatedWant = 0;
                    _calculatedHave = 0;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Clear amount and ratio");
                }

                ImGui.Separator();

                // Result Display
                if (_hasValidResult)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
                    ImGui.TextWrapped("Result (Want : Have):");
                    ImGui.PopStyleColor();

                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.2f, 1.0f));
                    ImGui.SetWindowFontScale(1.5f);
                    ImGui.Text($"{_calculatedWant} : {_calculatedHave}");
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopStyleColor();

                    ImGui.Spacing();

                    if (ImGui.Button("Fill Exchange Window", new Vector2(-1, 40)))
                    {
                        if (!_isProcessing)
                        {
                            _isProcessing = true;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await FillExchange(_calculatedWant, _calculatedHave);
                                }
                                finally
                                {
                                    _isProcessing = false;
                                }
                            });
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(_errorMessage))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.2f, 0.2f, 1.0f));
                    ImGui.TextWrapped($"Error: {_errorMessage}");
                    ImGui.PopStyleColor();
                }

                RenderFillStatus();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextWrapped(
                    _calculateExactWantedAmount
                        ? "Enter how much you want to buy and the ratio (Want:Have). The calculator shows how much you need to offer."
                        : "Enter your currency amount and the ratio (Want:Have). The calculator finds the maximum whole trade with no leftovers."
                );

                ImGui.Spacing();
                ImGui.Text("Examples:");
                ImGui.BulletText("1:3 = 1 wanted per 3 offered");
                ImGui.BulletText("2:5 = 2 wanted per 5 offered");

                // Competing Trades Section
                RenderCompetingTrades(Settings.ShowDebugInfo.Value);
            }
            ImGui.End();

            ImGui.PopStyleColor(2);
        }

        private void RenderCompetingTrades(bool showDebug)
        {
            var currencyPanel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
            if (currencyPanel == null)
                return;

            var stockList = currencyPanel.OfferedItemStock;
            if (stockList == null || !stockList.Any())
                return;

            var stockItems = stockList.ToList();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
            ImGui.Text("Competing Trades:");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Table header
            ImGui.Columns(3, "competing_trades", true);
            ImGui.SetColumnWidth(0, 80);
            ImGui.SetColumnWidth(1, 60);
            ImGui.SetColumnWidth(2, 300);

            ImGui.Text("Ratio");
            ImGui.NextColumn();
            ImGui.Text("Listed");
            ImGui.NextColumn();
            ImGui.Text("Actions");
            ImGui.NextColumn();
            ImGui.Separator();

            // Get player's available currency amount for the offered item
            int availableAmount = GetOfferedCurrencyAmount();

            int index = 0;
            foreach (var stock in stockItems)
            {
                if (stock == null)
                    continue;

                int get = stock.Get;
                int give = stock.Give;
                int listed = (int)stock.ListedCount;

                if (get <= 0 || give <= 0)
                    continue;

                if (
                    !RatioMath.TryCreateCompetingOrder(
                        get,
                        give,
                        out TradeRatio competingRatio
                    )
                )
                    continue;

                // Display ratio matching in-game format
                // In-game always displays the ratio with the larger value first (X:1 or 1:X where X >= 1)
                // The stock's Get/Give values represent the raw trade amounts
                string ratioDisplay;
                if (competingRatio.Want >= competingRatio.Have)
                {
                    double ratioValue = (double)competingRatio.Want / competingRatio.Have;
                    ratioDisplay = $"{ratioValue:F2}:1";
                }
                else
                {
                    double ratioValue = (double)competingRatio.Have / competingRatio.Want;
                    ratioDisplay = $"1:{ratioValue:F2}";
                }

                ImGui.Text(ratioDisplay);
                ImGui.NextColumn();
                ImGui.Text($"{listed}");
                ImGui.NextColumn();

                // Quick fill buttons
                ImGui.PushID(index);

                bool hasWantedAmount = TryGetWantedAmountInput(out int wantedAmount);
                var (matchWant, matchHave) = _calculateExactWantedAmount
                    ? hasWantedAmount
                        ? RatioMath.ExactFromWant(wantedAmount, competingRatio, availableAmount)
                        : (0, 0)
                    : RatioMath.MaxFromHave(availableAmount, competingRatio);

                if (matchHave > 0 && ImGui.SmallButton("Match"))
                {
                    QueueQuickFill(matchWant, matchHave);
                }
                else if (matchHave <= 0)
                {
                    ImGui.TextDisabled("Match");
                    if (
                        _calculateExactWantedAmount
                        && ImGui.IsItemHovered()
                    )
                    {
                        ImGui.SetTooltip(
                            hasWantedAmount
                                ? $"{wantedAmount} cannot make a whole trade at {competingRatio.Want}:{competingRatio.Have}, or you do not have enough offered currency."
                                : "Enter a positive exact amount first."
                        );
                    }
                }

                // Undercut buttons with percentages
                // Undercut means asking for LESS (reducing what you want/receive)
                // This makes your listing more attractive to buyers
                int[] undercutPercents = { 10, 20, 25, 30 };

                foreach (var percent in undercutPercents)
                {
                    ImGui.SameLine();

                    // Improve the competing buy order by wanting less for the same amount
                    // offered. Scale with integers so the resulting ratio remains exact.
                    bool hasUndercutRatio = RatioMath.TryUndercutWant(
                        competingRatio,
                        percent,
                        out TradeRatio undercutRatio
                    );
                    var (undercutWant, undercutHave) = hasUndercutRatio
                        ? _calculateExactWantedAmount
                            ? hasWantedAmount
                                ? RatioMath.ExactFromWant(
                                    wantedAmount,
                                    undercutRatio,
                                    availableAmount
                                )
                                : (0, 0)
                            : RatioMath.MaxFromHave(availableAmount, undercutRatio)
                        : (0, 0);

                    if (undercutHave > 0 && ImGui.SmallButton($"{percent}%"))
                    {
                        QueueQuickFill(undercutWant, undercutHave);
                    }
                    else if (undercutHave <= 0)
                    {
                        ImGui.TextDisabled($"{percent}%");
                        if (
                            _calculateExactWantedAmount
                            && ImGui.IsItemHovered()
                        )
                        {
                            ImGui.SetTooltip(
                                hasWantedAmount
                                    ? $"{wantedAmount} cannot make a whole trade at this adjusted ratio, or you do not have enough offered currency."
                                    : "Enter a positive exact amount first."
                            );
                        }
                    }
                }

                ImGui.PopID();
                ImGui.NextColumn();

                index++;
            }

            // Show available amount
            if (availableAmount > 0)
            {
                ImGui.Columns(1);
                ImGui.Spacing();
                ImGui.TextColored(
                    new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    $"Available to trade: {availableAmount}"
                );
            }

            ImGui.Columns(1);

            // Debug: Show all properties of first stock item
            if (showDebug && stockItems.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Text("Debug - First Stock Item Properties:");

                var firstStock = stockItems[0];
                if (firstStock != null)
                {
                    Type stockType = firstStock.GetType();
                    ImGui.Text($"Type: {stockType.FullName}");

                    foreach (
                        var prop in stockType.GetProperties(
                            BindingFlags.Public | BindingFlags.Instance
                        )
                    )
                    {
                        try
                        {
                            var value = prop.GetValue(firstStock);
                            ImGui.Text($"  {prop.Name}: {value}");
                        }
                        catch
                        {
                            ImGui.Text($"  {prop.Name}: <error>");
                        }
                    }
                }
            }

            // Process queued quick fill
            if (_quickFillWant > 0 && _quickFillHave > 0 && !_isProcessing)
            {
                _isProcessing = true;
                int want = _quickFillWant;
                int have = _quickFillHave;
                _quickFillWant = 0;
                _quickFillHave = 0;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FillExchange(want, have);
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                });
            }
        }

        private int GetOfferedCurrencyAmount()
        {
            try
            {
                var currencyPanel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
                if (currencyPanel == null)
                    return 0;

                var offeredItemType = currencyPanel.OfferedItemType;
                if (offeredItemType == null)
                    return 0;

                string targetMetadata = offeredItemType.Metadata;
                if (string.IsNullOrEmpty(targetMetadata))
                    return 0;

                var serverData = GameController.IngameState?.Data?.ServerData;
                var playerInventories = serverData?.PlayerInventories;
                if (playerInventories == null)
                    return 0;

                var mainInv = playerInventories
                    .FirstOrDefault(x => x?.Inventory?.InventType == InventoryTypeE.MainInventory);
                int inventoryAmount = CountMatchingItems(
                    mainInv?.Inventory?.Items,
                    targetMetadata
                );

                int serverStashAmount = 0;
                int visibleStashAmount = 0;
                if (Settings.IncludeStash.Value)
                {
                    foreach (var holder in playerInventories)
                    {
                        var stashInventory = holder?.Inventory;
                        if (stashInventory?.InventSlot != InventorySlotE.StashInventoryId)
                            continue;

                        serverStashAmount += CountMatchingItems(
                            stashInventory.Items,
                            targetMetadata
                        );
                    }

                    // Older/unloaded stash layouts may not expose a server inventory. Keep
                    // the visible tab as a fallback, but never add both representations.
                    if (serverStashAmount <= 0)
                    {
                        var visibleItems = GameController
                            .IngameState
                            ?.IngameUi
                            ?.StashElement
                            ?.VisibleStash
                            ?.VisibleInventoryItems;
                        if (visibleItems != null)
                        {
                            visibleStashAmount = CountMatchingItems(
                                visibleItems.Select(x => x?.Item),
                                targetMetadata
                            );
                        }
                    }
                }

                return CurrencyAmountMath.Combine(
                    inventoryAmount,
                    serverStashAmount,
                    visibleStashAmount,
                    Settings.IncludeStash.Value
                );
            }
            catch
            {
                return 0;
            }
        }

        private static int CountMatchingItems(
            IEnumerable<Entity> items,
            string targetMetadata
        )
        {
            if (items == null || string.IsNullOrEmpty(targetMetadata))
                return 0;

            long amount = 0;
            foreach (var item in items)
            {
                if (item?.Metadata != targetMetadata)
                    continue;

                amount += item.GetComponent<Stack>()?.Size ?? 1;
                if (amount >= int.MaxValue)
                    return int.MaxValue;
            }

            return (int)amount;
        }

        private bool TryGetWantedAmountInput(out int wantedAmount)
        {
            return int.TryParse(_amountInput, out wantedAmount) && wantedAmount > 0;
        }

        private void QueueQuickFill(int want, int have)
        {
            _quickFillWant = want;
            _quickFillHave = have;
        }

        // Shared hardened fill path for calculator and competing-trade actions.
        private async Task FillExchange(int wantValue, int haveValue)
        {
            if (wantValue <= 0 || haveValue <= 0)
                return;

            var currencyPanel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
            if (currencyPanel == null || !currencyPanel.IsVisible)
            {
                SetStatus("Currency Exchange panel is not open", false);
                return;
            }

            var wantedInput = currencyPanel.WantedItemCountInput;
            var offeredInput = currencyPanel.OfferedItemCountInput;
            if (wantedInput == null || offeredInput == null)
            {
                SetStatus("Input fields not found", false);
                LogMessage("Cannot fill: Input fields not found");
                return;
            }

            var windowOffsetSharp = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var windowOffset = new Vector2(windowOffsetSharp.X, windowOffsetSharp.Y);
            var offsets = new Vector2(Settings.ClickXOffset.Value, Settings.ClickYOffset.Value);
            var cursorBefore = Mouse.GetCursorPosition();
            bool aborted = false;
            bool placeReady = false;

            try
            {
                SetStatus("Filling...", true);

                bool wantOk = await FillField(wantedInput, wantValue, windowOffset, offsets);
                bool haveOk = await FillField(offeredInput, haveValue, windowOffset, offsets);
                placeReady = !Settings.VerifyFills.Value || (wantOk && haveOk);

                if (!Settings.VerifyFills.Value)
                {
                    SetStatus($"Filled (unverified): {wantValue} : {haveValue}", true);
                }
                else if (wantOk && haveOk)
                {
                    SetStatus($"Filled OK: {wantValue} : {haveValue}", true);
                }
                else
                {
                    string which =
                        (!wantOk ? "Want" : "")
                        + (!wantOk && !haveOk ? " & " : "")
                        + (!haveOk ? "Have" : "");
                    SetStatus($"Could not verify {which} - check the fields and retry", false);
                }

                LogMessage(
                    $"Fill want={wantValue} have={haveValue} wantOk={wantOk} haveOk={haveOk}"
                );
            }
            catch (FillAbortedException ex)
            {
                aborted = true;
                SetStatus($"Fill cancelled ({ex.Message})", false);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", false);
                LogError($"Error during fill: {ex.Message}");
            }
            finally
            {
                if (aborted)
                {
                    // Leave the cursor under the user's control after a cancellation.
                }
                else if (placeReady && Settings.MoveCursorToPlaceOrder.Value)
                {
                    if (
                        TryGetPlaceOrderButtonPos(
                            currencyPanel,
                            windowOffset,
                            offsets,
                            out var placePos
                        )
                    )
                    {
                        Mouse.SetPosition(placePos);
                    }
                    else
                    {
                        Mouse.SetPosition(new Vector2(cursorBefore.X, cursorBefore.Y));
                        if (Settings.ShowDebugInfo.Value)
                            LogMessage("Place Order button not found; cursor restored.");
                    }
                }
                else
                {
                    Mouse.SetPosition(new Vector2(cursorBefore.X, cursorBefore.Y));
                }
            }
        }

        private bool TryGetPlaceOrderButtonPos(
            ExileCore.PoEMemory.Element panel,
            Vector2 windowOffset,
            Vector2 offsets,
            out Vector2 pos
        )
        {
            pos = default;

            try
            {
                var label = FindPlaceOrderLabel(panel) ?? FindPlaceOrderLabel(panel?.Root);
                var button = label?.Parent ?? label;
                if (button == null || !button.IsValid || !button.IsVisible)
                    return false;

                var rect = button.GetClientRectCache;
                if (rect.Width <= 1 || rect.Height <= 1)
                    return false;

                var center = rect.Center;
                pos = new Vector2(center.X, center.Y) + windowOffset + offsets;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ExileCore.PoEMemory.Element FindPlaceOrderLabel(
            ExileCore.PoEMemory.Element root
        )
        {
            return root?.FindChildRecursive(e =>
            {
                var text = e?.TextNoTags;
                if (string.IsNullOrEmpty(text))
                    text = e?.Text;
                return !string.IsNullOrEmpty(text)
                    && text.Trim().Equals(PlaceOrderLabelText, StringComparison.OrdinalIgnoreCase);
            });
        }

        private async Task<bool> FillField(
            ExileCore.PoEMemory.Element field,
            int value,
            Vector2 windowOffset,
            Vector2 offsets
        )
        {
            bool verify = Settings.VerifyFills.Value;
            int maxAttempts = verify ? Math.Max(1, Settings.MaxFillRetries.Value) : 1;
            string target = value.ToString();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var center = field.GetClientRectCache.Center;
                var pos = new Vector2(center.X, center.Y) + windowOffset + offsets;

                await ClickAt(pos);
                await Task.Delay(ClickFocusDelayMs);
                EnsureCursorHeld(pos);
                await ClickAt(pos);
                await Task.Delay(ClickFocusDelayMs);
                EnsureCursorHeld(pos);

                for (int i = 0; i < ClearBackspaces; i++)
                {
                    EnsureCursorHeld(pos);
                    await Keyboard.KeyPress(Keys.Back);
                }

                foreach (char character in target)
                {
                    EnsureCursorHeld(pos);
                    await Keyboard.Type(character.ToString());
                }
                EnsureCursorHeld(pos);

                if (!verify)
                {
                    await Task.Delay(150);
                    return true;
                }

                if (await WaitForFieldValue(field, value, pos))
                    return true;
            }

            return false;
        }

        private static async Task ClickAt(Vector2 pos)
        {
            await Mouse.MoveMouse(pos);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
        }

        private async Task<bool> WaitForFieldValue(
            ExileCore.PoEMemory.Element field,
            int target,
            Vector2 heldPos
        )
        {
            int elapsed = 0;
            while (elapsed < VerifyTimeoutMs)
            {
                EnsureCursorHeld(heldPos);

                var read = ReadFieldNumber(field);
                if (read.HasValue && read.Value == target)
                    return true;

                await Task.Delay(VerifyPollMs);
                elapsed += VerifyPollMs;
            }

            return false;
        }

        private static int? ReadFieldNumber(ExileCore.PoEMemory.Element field)
        {
            if (field == null)
                return null;

            try
            {
                string raw = FirstNonEmpty(field.TextNoTags, field.Text);

                if (string.IsNullOrWhiteSpace(raw))
                {
                    var child = field.FindChildRecursive(e =>
                    {
                        var text = e?.Text;
                        return !string.IsNullOrWhiteSpace(text) && text.Any(char.IsDigit);
                    });
                    if (child != null)
                        raw = FirstNonEmpty(child.TextNoTags, child.Text);
                }

                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                var digits = new string(raw.Where(char.IsDigit).ToArray());
                return digits.Length > 0 && int.TryParse(digits, out int value)
                    ? value
                    : (int?)null;
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonEmpty(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) ? a : b;

        private void EnsureCursorHeld(Vector2 expected)
        {
            if (!Settings.AbortOnMouseMove.Value)
                return;

            var current = Mouse.GetCursorPosition();
            int dx = current.X - (int)expected.X;
            int dy = current.Y - (int)expected.Y;
            if (dx * dx + dy * dy > CursorDriftThresholdSq)
                throw new FillAbortedException("you moved the mouse");
        }

        private void RenderFillStatus()
        {
            if (_isProcessing)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.85f, 0.2f, 1.0f));
                ImGui.TextWrapped("Filling exchange window...");
                ImGui.PopStyleColor();
                return;
            }

            if (string.IsNullOrEmpty(_statusMessage))
                return;

            var color = _statusOk
                ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
                : new Vector4(1.0f, 0.4f, 0.4f, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(_statusMessage);
            ImGui.PopStyleColor();
        }

        private void SetStatus(string message, bool ok)
        {
            _statusMessage = message;
            _statusOk = ok;
        }

        private sealed class FillAbortedException : Exception
        {
            public FillAbortedException(string message)
                : base(message) { }
        }

        private void Calculate()
        {
            _hasValidResult = false;
            _errorMessage = "";

            if (string.IsNullOrWhiteSpace(_amountInput) || string.IsNullOrWhiteSpace(_ratioInput))
            {
                return;
            }

            // Parse amount
            if (!int.TryParse(_amountInput, out int amount) || amount <= 0)
            {
                _errorMessage = "Currency amount should be a positive integer";
                return;
            }

            // Parse ratio (Want:Have format)
            if (!RatioMath.TryParse(_ratioInput, out TradeRatio ratio))
            {
                _errorMessage = "Error parsing ratio. Use format like 1:3 or 2:5";
                return;
            }

            var result = _calculateExactWantedAmount
                ? RatioMath.ExactFromWant(amount, ratio)
                : RatioMath.MaxFromHave(amount, ratio);

            if (result.want > 0 && result.have > 0)
            {
                _calculatedWant = result.want;
                _calculatedHave = result.have;
                _hasValidResult = true;
            }
            else
            {
                _errorMessage = _calculateExactWantedAmount
                    ? $"{amount} cannot make a whole trade at {ratio.Want}:{ratio.Have}. The wanted amount must be a multiple of {ratio.Want}."
                    : $"No whole trade fits. This ratio requires at least {ratio.Have} offered currency.";
            }
        }

    }
}
