using System;

namespace CurrencyRatioExchange
{
    internal static class CurrencyAmountMath
    {
        public static int Combine(
            int inventoryAmount,
            int serverStashAmount,
            int visibleStashAmount,
            bool includeStash
        )
        {
            long total = Math.Max(0, inventoryAmount);
            if (includeStash)
            {
                total += serverStashAmount > 0
                    ? serverStashAmount
                    : Math.Max(0, visibleStashAmount);
            }

            return (int)Math.Min(total, int.MaxValue);
        }
    }
}
