using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CurrencyRatioExchange
{
    internal readonly record struct TradeRatio(int Want, int Have);

    internal static class RatioMath
    {
        public static bool TryCreate(long want, long have, out TradeRatio ratio)
        {
            ratio = default;
            if (want <= 0 || have <= 0)
                return false;

            long divisor = GreatestCommonDivisor(want, have);
            want /= divisor;
            have /= divisor;

            if (want > int.MaxValue || have > int.MaxValue)
                return false;

            ratio = new TradeRatio((int)want, (int)have);
            return true;
        }

        public static bool TryCreateCompetingOrder(
            int stockGet,
            int stockGive,
            out TradeRatio ratio
        )
        {
            // OfferedItemStock is expressed from the competing order's side:
            // they Give what we Want and Get what we Have.
            return TryCreate(stockGive, stockGet, out ratio);
        }

        public static bool TryParse(string input, out TradeRatio ratio)
        {
            ratio = default;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                input = input.Trim();
                if (input.Contains(':'))
                {
                    string[] parts = input.Split(':');
                    return parts.Length == 2
                        && TryParseDecimal(parts[0], out decimal want)
                        && TryParseDecimal(parts[1], out decimal have)
                        && TryCreate(want, have, out ratio);
                }

                if (!Regex.IsMatch(input, @"^[\d\s.\/ +\-*()]+$"))
                    return false;

                var table = new DataTable();
                object result = table.Compute(input, null);
                decimal evaluated = Convert.ToDecimal(result, CultureInfo.InvariantCulture);
                return TryCreate(1m, evaluated, out ratio);
            }
            catch
            {
                return false;
            }
        }

        public static (int want, int have) MaxFromHave(int availableHave, TradeRatio ratio)
        {
            if (availableHave <= 0 || ratio.Want <= 0 || ratio.Have <= 0)
                return (0, 0);

            long lots = availableHave / ratio.Have;
            lots = Math.Min(lots, int.MaxValue / (long)ratio.Want);
            if (lots <= 0)
                return (0, 0);

            return ((int)(lots * ratio.Want), (int)(lots * ratio.Have));
        }

        public static (int want, int have) ExactFromWant(
            int wantedAmount,
            TradeRatio ratio,
            int availableHave = int.MaxValue
        )
        {
            if (
                wantedAmount <= 0
                || availableHave <= 0
                || ratio.Want <= 0
                || ratio.Have <= 0
                || wantedAmount % ratio.Want != 0
            )
            {
                return (0, 0);
            }

            long lots = wantedAmount / ratio.Want;
            long requiredHave = lots * ratio.Have;
            if (requiredHave <= 0 || requiredHave > availableHave || requiredHave > int.MaxValue)
                return (0, 0);

            return (wantedAmount, (int)requiredHave);
        }

        public static bool TryUndercutWant(
            TradeRatio ratio,
            int percent,
            out TradeRatio undercutRatio
        )
        {
            undercutRatio = default;
            if (percent <= 0 || percent >= 100)
                return false;

            try
            {
                long want = checked((long)ratio.Want * (100 - percent));
                long have = checked((long)ratio.Have * 100);
                return TryCreate(want, have, out undercutRatio);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryCreate(decimal want, decimal have, out TradeRatio ratio)
        {
            ratio = default;
            if (want <= 0 || have <= 0)
                return false;

            try
            {
                int scale = Math.Max(GetScale(want), GetScale(have));
                decimal multiplier = 1m;
                for (int i = 0; i < scale; i++)
                    multiplier *= 10m;

                decimal scaledWant = want * multiplier;
                decimal scaledHave = have * multiplier;
                if (scaledWant > long.MaxValue || scaledHave > long.MaxValue)
                    return false;

                return TryCreate(decimal.ToInt64(scaledWant), decimal.ToInt64(scaledHave), out ratio);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryParseDecimal(string input, out decimal value)
        {
            const NumberStyles styles = NumberStyles.Number;
            return decimal.TryParse(input.Trim(), styles, CultureInfo.CurrentCulture, out value)
                || decimal.TryParse(input.Trim(), styles, CultureInfo.InvariantCulture, out value);
        }

        private static int GetScale(decimal value)
        {
            return (decimal.GetBits(value)[3] >> 16) & 0x7F;
        }

        private static long GreatestCommonDivisor(long left, long right)
        {
            while (right != 0)
            {
                long remainder = left % right;
                left = right;
                right = remainder;
            }

            return Math.Abs(left);
        }
    }
}
