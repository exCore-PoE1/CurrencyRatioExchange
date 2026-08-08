using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CurrencyRatioExchange.Tests
{
    [TestClass]
    public class RatioMathTests
    {
        [TestMethod]
        public void MaxFromHave_DoesNotAcceptNearWholeTrade()
        {
            Assert.IsTrue(RatioMath.TryCreate(1, 2001, out TradeRatio ratio));

            Assert.AreEqual((0, 0), RatioMath.MaxFromHave(2000, ratio));
            Assert.AreEqual((1, 2001), RatioMath.MaxFromHave(2001, ratio));
        }

        [TestMethod]
        public void ExactFromWant_RejectsAmountOutsideRatioLots()
        {
            Assert.IsTrue(RatioMath.TryCreate(5, 9, out TradeRatio ratio));

            Assert.AreEqual((0, 0), RatioMath.ExactFromWant(7, ratio));
            Assert.AreEqual((10, 18), RatioMath.ExactFromWant(10, ratio));
        }

        [TestMethod]
        public void ExactFromWant_RejectsTradeBeyondAvailableCurrency()
        {
            Assert.IsTrue(RatioMath.TryCreate(5, 9, out TradeRatio ratio));

            Assert.AreEqual((0, 0), RatioMath.ExactFromWant(10, ratio, 17));
            Assert.AreEqual((10, 18), RatioMath.ExactFromWant(10, ratio, 18));
        }

        [TestMethod]
        public void CompetingOrder_MapsGiveToWantAndGetToHave()
        {
            // Captured from a live row that represents 451 wanted for 20,340 offered.
            const int stockGet = 20340;
            const int stockGive = 451;

            Assert.IsTrue(
                RatioMath.TryCreateCompetingOrder(stockGet, stockGive, out TradeRatio ratio)
            );
            Assert.AreEqual(new TradeRatio(451, 20340), ratio);
            Assert.AreEqual((451, 20340), RatioMath.MaxFromHave(20340, ratio));
        }

        [TestMethod]
        public void Undercut_RemainsAnExactReducedRatio()
        {
            Assert.IsTrue(RatioMath.TryCreate(27, 20, out TradeRatio ratio));
            Assert.IsTrue(RatioMath.TryUndercutWant(ratio, 10, out TradeRatio undercut));

            Assert.AreEqual(new TradeRatio(243, 200), undercut);
            Assert.AreEqual((243, 200), RatioMath.MaxFromHave(200, undercut));
            Assert.AreEqual((0, 0), RatioMath.MaxFromHave(199, undercut));
        }

        [DataTestMethod]
        [DataRow("1:2001", 1, 2001)]
        [DataRow("1.5:3", 1, 2)]
        [DataRow("2:5", 2, 5)]
        public void TryParse_PreservesExactRatio(string input, int want, int have)
        {
            Assert.IsTrue(RatioMath.TryParse(input, out TradeRatio ratio));
            Assert.AreEqual(new TradeRatio(want, have), ratio);
        }
    }
}
