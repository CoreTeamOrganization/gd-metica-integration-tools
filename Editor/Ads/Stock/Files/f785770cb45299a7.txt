#if UNITY_EDITOR

using UnityEngine;

namespace Monetization.Runtime.Utilities
{
    public class EditorToastService : IToastService
    {
        public void Show(string message, bool isLong)
        {
            Debug.Log($"Toast: {message}");
        }
    }
}
#endif
