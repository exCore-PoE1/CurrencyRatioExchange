using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using CurrencyRatioExchange.Utils;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
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
        private bool _isProcessing = false;
        private bool _calculateExactWantedAmount = false;

        // For quick-fill from competing trades
        private int _quickFillWant = 0;
        private int _quickFillHave = 0;

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
                                    await PerformFill();
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

                // Keep as doubles to preserve ratio precision
                double get = stock.Get;
                double give = stock.Give;
                int listed = (int)stock.ListedCount;

                if (get <= 0 || give <= 0)
                    continue;

                // Display ratio matching in-game format
                // In-game always displays the ratio with the larger value first (X:1 or 1:X where X >= 1)
                // The stock's Get/Give values represent the raw trade amounts
                string ratioDisplay;
                if (give >= get)
                {
                    // give/get >= 1, display as X:1 (e.g., 2.20:1)
                    double ratioValue = give / get;
                    ratioDisplay = $"{ratioValue:F2}:1";
                }
                else
                {
                    // get/give > 1, display as 1:X (e.g., 1:1.33)
                    double ratioValue = get / give;
                    ratioDisplay = $"1:{ratioValue:F2}";
                }

                ImGui.Text(ratioDisplay);
                ImGui.NextColumn();
                ImGui.Text($"{listed}");
                ImGui.NextColumn();

                // Quick fill buttons
                ImGui.PushID(index);

                // Calculate actual fill amounts.
                // - give = what we want to receive
                // - get = what we have to offer
                bool hasWantedAmount = TryGetWantedAmountInput(out int wantedAmount);
                var (matchWant, matchHave) =
                    _calculateExactWantedAmount && hasWantedAmount
                        ? CalculateApproximateWantedTrade(
                            wantedAmount,
                            give,
                            get,
                            availableAmount
                        )
                        : CalculateFillAmounts(give, get, availableAmount);

                if (matchHave > 0 && ImGui.SmallButton("Match"))
                {
                    QueueQuickFill(matchWant, matchHave);
                }
                else if (matchHave <= 0)
                {
                    ImGui.TextDisabled("Match");
                }

                // Undercut buttons with percentages
                // Undercut means asking for LESS (reducing what you want/receive)
                // This makes your listing more attractive to buyers
                int[] undercutPercents = { 10, 20, 25, 30 };

                foreach (var percent in undercutPercents)
                {
                    ImGui.SameLine();

                    // Calculate undercut: reduce 'give' (what we receive) by the percentage
                    double undercutGive = give * (1.0 - percent / 100.0);
                    var (undercutWant, undercutHave) =
                        _calculateExactWantedAmount && hasWantedAmount
                            ? CalculateApproximateWantedTrade(
                                wantedAmount,
                                undercutGive,
                                get,
                                availableAmount
                            )
                            : CalculateFillAmounts(undercutGive, get, availableAmount);

                    if (undercutHave > 0 && ImGui.SmallButton($"{percent}%"))
                    {
                        QueueQuickFill(undercutWant, undercutHave);
                    }
                    else if (undercutHave <= 0)
                    {
                        ImGui.TextDisabled($"{percent}%");
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
                        await PerformFillValues(want, have);
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

                string targetBaseName = offeredItemType.BaseName;
                if (string.IsNullOrEmpty(targetBaseName))
                    return 0;

                int amount = 0;

                var mainInv = GameController
                    .IngameState
                    .Data
                    .ServerData
                    .PlayerInventories
                    .FirstOrDefault(x => x?.Inventory?.InventType == InventoryTypeE.MainInventory);

                var inventory = mainInv?.Inventory;
                if (inventory?.Items == null)
                    return 0;

                foreach (var item in inventory.Items)
                {
                    if (item == null)
                        continue;

                    var baseItemType = GameController.Files.BaseItemTypes.Translate(
                        item.Metadata
                    );
                    if (baseItemType?.BaseName == targetBaseName)
                    {
                        var stackComp = item.GetComponent<Stack>();
                        amount += stackComp?.Size ?? 1;
                    }
                }

                // Also check visible stash tab if enabled
                if (Settings.IncludeStash)
                {
                    try
                    {
                        var stashElement = GameController.IngameState?.IngameUi?.StashElement;
                        var visibleStash = stashElement?.VisibleStash;
                        var stashItems = visibleStash?.VisibleInventoryItems;
                        if (stashItems != null)
                        {
                            foreach (var item in stashItems)
                            {
                                if (item?.Item == null)
                                    continue;

                                var baseItemType = GameController.Files.BaseItemTypes.Translate(
                                    item.Item.Metadata
                                );
                                if (baseItemType?.BaseName == targetBaseName)
                                {
                                    var stackComp = item.Item.GetComponent<Stack>();
                                    amount += stackComp?.Size ?? 1;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Stash not available, just use inventory amount
                    }
                }

                return amount;
            }
            catch
            {
                return 0;
            }
        }

        private (int want, int have) CalculateFillAmounts(
            double wantPerUnit,
            double havePerUnit,
            int availableAmount
        )
        {
            if (availableAmount <= 0 || wantPerUnit <= 0 || havePerUnit <= 0)
                return (0, 0);

            // Called with (give, get, availableAmount) from the stock item:
            // - wantPerUnit = stock.Give = what WE receive per ratio unit
            // - havePerUnit = stock.Get = what WE offer per ratio unit
            //
            // We want to maximize based on OUR available stock while maintaining the ratio.
            // Use the ratio as a decimal and find the largest 'have' value where 'want' is whole.

            double ratio = wantPerUnit / havePerUnit;

            // Start from max available and find largest 'have' that produces whole 'want'
            for (int have = availableAmount; have > 0; have--)
            {
                double wantExact = have * ratio;
                int wantRounded = (int)Math.Round(wantExact);

                // Check if it's effectively a whole number
                if (wantRounded > 0 && Math.Abs(wantExact - wantRounded) < 0.001)
                {
                    return (wantRounded, have);
                }
            }

            return (0, 0);
        }

        private bool TryGetWantedAmountInput(out int wantedAmount)
        {
            return int.TryParse(_amountInput, out wantedAmount) && wantedAmount > 0;
        }

        private (int want, int have) CalculateApproximateWantedTrade(
            int wantedAmount,
            double wantPerUnit,
            double havePerUnit,
            int availableAmount
        )
        {
            if (wantedAmount <= 0 || wantPerUnit <= 0 || havePerUnit <= 0 || availableAmount <= 0)
                return (0, 0);

            int haveRoundedUp = (int)Math.Ceiling(wantedAmount * havePerUnit / wantPerUnit);
            if (haveRoundedUp <= 0 || haveRoundedUp > availableAmount)
                return (0, 0);

            return (wantedAmount, haveRoundedUp);
        }

        private void QueueQuickFill(int want, int have)
        {
            _quickFillWant = want;
            _quickFillHave = have;
        }

        private async Task PerformFillValues(int wantValue, int haveValue)
        {
            var currencyPanel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
            if (currencyPanel == null || !currencyPanel.IsVisible)
                return;

            var wantedInput = currencyPanel.WantedItemCountInput;
            var offeredInput = currencyPanel.OfferedItemCountInput;

            if (wantedInput == null || offeredInput == null)
            {
                LogMessage("Cannot fill: Input fields not found");
                return;
            }

            try
            {
                var windowOffsetSharp = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                var windowOffset = new Vector2(windowOffsetSharp.X, windowOffsetSharp.Y);
                var xOffset = Settings.ClickXOffset.Value;
                var yOffset = Settings.ClickYOffset.Value;

                // Fill the "I Want" field
                var wantCenter = wantedInput.GetClientRectCache.Center;
                var wantPos = new Vector2(wantCenter.X, wantCenter.Y) + windowOffset + new Vector2(xOffset, yOffset);

                await Mouse.MoveMouse(wantPos);
                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                for (int i = 0; i < 8; i++)
                {
                    await Keyboard.KeyPress(Keys.Back);
                }

                await Keyboard.Type(wantValue.ToString());
                await Task.Delay(150);

                // Fill the "I Have" field
                var haveCenter = offeredInput.GetClientRectCache.Center;
                var havePos = new Vector2(haveCenter.X, haveCenter.Y) + windowOffset + new Vector2(xOffset, yOffset);

                await Mouse.MoveMouse(havePos);
                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                for (int i = 0; i < 8; i++)
                {
                    await Keyboard.KeyPress(Keys.Back);
                }

                await Keyboard.Type(haveValue.ToString());

                LogMessage($"Filled: Want {wantValue} : Have {haveValue}");
            }
            catch (Exception ex)
            {
                LogError($"Error during fill: {ex.Message}");
            }
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
            var ratioParts = ParseRatio(_ratioInput);
            if (ratioParts == null)
            {
                _errorMessage = "Error parsing ratio. Use format like 1:3 or 2:5";
                return;
            }

            double wantPart = ratioParts.Value.want;
            double havePart = ratioParts.Value.have;

            if (wantPart <= 0 || havePart <= 0)
            {
                _errorMessage = "Ratio values must be positive";
                return;
            }

            var result = _calculateExactWantedAmount
                ? CalculateExactWantedTrade(amount, wantPart, havePart)
                : CalculateMaxTrade(amount, wantPart / havePart);

            if (result.want > 0 && result.have > 0)
            {
                _calculatedWant = result.want;
                _calculatedHave = result.have;
                _hasValidResult = true;
            }
            else
            {
                _errorMessage = "No valid trade possible";
            }
        }

        private (double want, double have)? ParseRatio(string ratioStr)
        {
            if (string.IsNullOrWhiteSpace(ratioStr))
                return null;

            try
            {
                ratioStr = ratioStr.Trim();

                // Check for colon separator (Want:Have format)
                if (ratioStr.Contains(':'))
                {
                    var parts = ratioStr.Split(':');
                    if (parts.Length != 2)
                        return null;

                    if (
                        double.TryParse(parts[0].Trim(), out double want)
                        && double.TryParse(parts[1].Trim(), out double have)
                    )
                    {
                        return (want, have);
                    }
                }
                else
                {
                    // Try to parse as single expression (e.g., "1.5" or "3/2")
                    // Check if it contains only valid characters for math expressions
                    if (
                        !System.Text.RegularExpressions.Regex.IsMatch(
                            ratioStr,
                            @"^[\d\s.\/+\-*()]+$"
                        )
                    )
                    {
                        return null;
                    }

                    // Use DataTable to evaluate the expression
                    var table = new DataTable();
                    var result = table.Compute(ratioStr, null);

                    if (result is decimal || result is double || result is int)
                    {
                        double ratio = Convert.ToDouble(result);
                        if (double.IsFinite(ratio) && ratio > 0)
                        {
                            // Convert single ratio to Want:Have format (1:ratio)
                            return (1, ratio);
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private (int want, int have) CalculateMaxTrade(int totalAmount, double ratio)
        {
            // ratio = want / have
            // We need to find the largest 'have' value where (have * ratio) is a whole number

            for (int have = totalAmount; have > 0; have--)
            {
                double want = have * ratio;

                // Check if want is effectively a whole number (accounting for floating point precision)
                if (Math.Abs(want - Math.Round(want)) < 0.000001)
                {
                    int wantRounded = (int)Math.Round(want);

                    if (wantRounded > 0)
                    {
                        return (want: wantRounded, have: have);
                    }
                }
            }

            return (want: 0, have: 0);
        }

        private (int want, int have) CalculateExactWantedTrade(
            int wantedAmount,
            double wantPart,
            double havePart
        )
        {
            int haveRoundedUp = (int)Math.Ceiling(wantedAmount * havePart / wantPart);
            if (haveRoundedUp > 0)
                return (want: wantedAmount, have: haveRoundedUp);

            return (want: 0, have: 0);
        }

        private async Task PerformFill()
        {
            var currencyPanel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
            if (currencyPanel == null || !currencyPanel.IsVisible)
                return;

            if (!_hasValidResult || _calculatedWant <= 0 || _calculatedHave <= 0)
            {
                return;
            }

            var wantedInput = currencyPanel.WantedItemCountInput;
            var offeredInput = currencyPanel.OfferedItemCountInput;

            if (wantedInput == null || offeredInput == null)
            {
                LogMessage("Cannot fill: Input fields not found");
                return;
            }

            try
            {
                var windowOffsetSharp = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                var windowOffset = new Vector2(windowOffsetSharp.X, windowOffsetSharp.Y);
                var xOffset = Settings.ClickXOffset.Value;
                var yOffset = Settings.ClickYOffset.Value;

                // Fill the "I Want" field
                var wantCenter = wantedInput.GetClientRectCache.Center;
                var wantPos = new Vector2(wantCenter.X, wantCenter.Y) + windowOffset + new Vector2(xOffset, yOffset);

                // Double-click to ensure focus (first click focuses game, second click focuses field)
                await Mouse.MoveMouse(wantPos);
                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                // Clear field
                for (int i = 0; i < 8; i++)
                {
                    await Keyboard.KeyPress(Keys.Back);
                }

                await Keyboard.Type(_calculatedWant.ToString());
                await Task.Delay(150);

                // Fill the "I Have" field
                var haveCenter = offeredInput.GetClientRectCache.Center;
                var havePos = new Vector2(haveCenter.X, haveCenter.Y) + windowOffset + new Vector2(xOffset, yOffset);

                // Double-click for this field too
                await Mouse.MoveMouse(havePos);
                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                await Mouse.LeftDown();
                await Mouse.LeftUp();
                await Task.Delay(100);

                // Clear field
                for (int i = 0; i < 8; i++)
                {
                    await Keyboard.KeyPress(Keys.Back);
                }

                await Keyboard.Type(_calculatedHave.ToString());

                LogMessage($"Filled: Want {_calculatedWant} : Have {_calculatedHave}");
            }
            catch (Exception ex)
            {
                LogError($"Error during fill: {ex.Message}");
            }
        }
    }
}
