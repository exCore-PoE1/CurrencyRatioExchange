using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CurrencyRatioExchange.Utils
{
    internal class Keyboard
    {
        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern uint keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        public static async Task KeyDown(Keys key)
        {
            keybd_event((byte)key, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            await Task.Delay(10);
        }

        public static async Task KeyUp(Keys key)
        {
            keybd_event((byte)key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
            await Task.Delay(10);
        }

        public static async Task Type(string text)
        {
            foreach (char c in text)
            {
                Keys key = (Keys)char.ToUpper(c);
                if (char.IsLetterOrDigit(c))
                {
                    await KeyDown(key);
                    await KeyUp(key);
                }
            }
        }

        public static async Task KeyPress(Keys key)
        {
            await KeyDown(key);
            await KeyUp(key);
        }
    }
}
