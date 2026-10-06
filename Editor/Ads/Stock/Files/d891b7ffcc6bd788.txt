#if UNITY_ANDROID

using UnityEngine;

namespace Monetization.Runtime.Utilities
{
    public class AndroidToastService : IToastService
    {
        public void Show(string message, bool isLong)
        {
            using var toastClass = new AndroidJavaClass("android.widget.Toast");
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");

            AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            if (activity == null)
            {
                Debug.LogWarning("AndroidToastService: currentActivity is null");
                return;
            }

            var toast = toastClass.CallStatic<AndroidJavaObject>(
                "makeText",
                activity,
                message,
                toastClass.GetStatic<int>(isLong ? "LENGTH_LONG" : "LENGTH_SHORT")
            );

            toast.Call("show");
        }
    }
}
#endif
