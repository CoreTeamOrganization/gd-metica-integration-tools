namespace Monetization.Runtime.Utilities
{
    public static class ToastServiceFactory
    {
        public static IToastService Create()
        {
#if UNITY_EDITOR
            return new EditorToastService();
#elif UNITY_ANDROID
            return new AndroidToastService();
#elif UNITY_IOS
            return new IOSToastService();
#else
            return new EditorToastService();
#endif
        }
    }
}
