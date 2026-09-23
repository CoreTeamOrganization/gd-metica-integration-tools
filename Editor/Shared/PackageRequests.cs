using System;
using UnityEditor;
using UnityEditor.PackageManager.Requests;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Waits on a Package Manager request the documented way — polled from
    /// EditorApplication.update — rather than blocking the main thread in a loop, which is not
    /// guaranteed to let the request finish.
    /// </summary>
    internal static class PackageRequests
    {
        public static void Track(Request request, string title, Action<Request> done)
        {
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!request.IsCompleted)
                {
                    EditorUtility.DisplayProgressBar(title, "Working…", 0.5f);
                    return;
                }

                EditorApplication.update -= poll;
                EditorUtility.ClearProgressBar();
                done(request);
            };

            EditorApplication.update += poll;
        }
    }
}
