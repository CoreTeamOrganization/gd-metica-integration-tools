#if UNITY_IOS

using System.Runtime.InteropServices;

namespace Monetization.Runtime.Utilities
{
    public class IOSToastService : IToastService
    {
        [DllImport("__Internal")]
        private static extern void showToast(string msg, bool isLong);

        public void Show(string message, bool isLong)
        {
            showToast(message, isLong);
        }
    }
}
#endif
