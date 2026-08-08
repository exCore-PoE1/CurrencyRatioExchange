using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CurrencyRatioExchange.Tests
{
    [TestClass]
    public class CurrencyAmountMathTests
    {
        [TestMethod]
        public void Combine_UsesServerStashWithoutDoubleCountingVisibleTab()
        {
            int amount = CurrencyAmountMath.Combine(
                inventoryAmount: 20,
                serverStashAmount: 5629,
                visibleStashAmount: 5629,
                includeStash: true
            );

            Assert.AreEqual(5649, amount);
        }

        [TestMethod]
        public void Combine_FallsBackToVisibleTabWhenServerStashIsUnavailable()
        {
            int amount = CurrencyAmountMath.Combine(
                inventoryAmount: 20,
                serverStashAmount: 0,
                visibleStashAmount: 5629,
                includeStash: true
            );

            Assert.AreEqual(5649, amount);
        }

        [TestMethod]
        public void Combine_ExcludesEveryStashSourceWhenDisabled()
        {
            int amount = CurrencyAmountMath.Combine(
                inventoryAmount: 20,
                serverStashAmount: 5629,
                visibleStashAmount: 5629,
                includeStash: false
            );

            Assert.AreEqual(20, amount);
        }
    }
}
