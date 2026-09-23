#nullable enable
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using UnityEngine.Serialization;
#if METICA_ANALYTICS
using Metica;
#endif

namespace GameDistrict.MeticaAnalytics
{
    public class GDMeticaAnalytics : ScriptableObject
    {
        [Header("Metica Configuration")]
        [SerializeField] private string appId     = "";
        [SerializeField] private string apiKey    = "";
        
        [Tooltip("Leave empty to use SystemInfo.deviceUniqueIdentifier")]
        [SerializeField] private string userId    = "";

        [Header("Debug")]
        [SerializeField] private bool enableMeticaLogs = false;

        [Header("Adjust Information")]
        [SerializeField] private string adId      = "";
        [SerializeField] private string appToken  = "";

        public string? AbTest          { get; set; } = null;
        public string? AbGroup         { get; set; } = null;
        public string? AbTestStartDate { get; set; } = null;
        
        /// <summary>
        /// Manually initialize Metica SDK. Call this when Auto Initialize is unchecked.
        /// Optionally pass a userId to override the inspector value and SystemInfo.deviceUniqueIdentifier.
        /// </summary>
        public void Initialize(string? overrideUserId = null)
        {
#if METICA_ANALYTICS
            RunInit(overrideUserId);
#endif
        }

        private void RunInit(string? overrideUserId)
        {
#if METICA_ANALYTICS
            var resolvedUserId = !string.IsNullOrEmpty(overrideUserId) ? overrideUserId
                : !string.IsNullOrEmpty(userId) ? userId
                : SystemInfo.deviceUniqueIdentifier;

            MeticaSdk.SetLogEnabled(enableMeticaLogs);
            MeticaSdk.InitializeAnalytics(new MeticaInitConfig(apiKey, appId, resolvedUserId));
#endif
        }

        /// <summary>
        /// Update the Adjust ad ID and app token after async Adjust initialization.
        /// </summary>
        public void UpdateAdjustInfo(string adId, string appToken)
        {
            this.adId      = adId;
            this.appToken  = appToken;
        }

        /// <summary>
        /// Forwards to GDPerformance.Configure. Call once, after remote config is fetched;
        /// overrides the GDPerfTracker prefab's Inspector defaults. No-op if GD Performance
        /// Tracker isn't installed (see README).
        /// </summary>
        public void ConfigurePerformanceTracking(bool perfEnabled, float sampleIntervalSeconds = 1f, bool startupEnabled = false)
        {
#if GD_PERFORMANCE_TRACKER
            GDPerformance.Configure(perfEnabled, sampleIntervalSeconds, startupEnabled);
#endif
        }

        /// <summary>
        /// Forwards to GDPerformance.MarkGameInteractive. Call once, the moment the game is
        /// genuinely playable. Idempotent. No-op if GD Performance Tracker isn't installed.
        /// </summary>
        public void MarkGameInteractive()
        {
#if GD_PERFORMANCE_TRACKER
            GDPerformance.MarkGameInteractive();
#endif
        }

        public virtual Dictionary<string, object> CreateBaseEvent()
        {
            return WithAbTestFields(new Dictionary<string, object>
            {
                { "adid",      adId },
                { "appToken", appToken },
            });
        }

        private Dictionary<string, object> WithAbTestFields(Dictionary<string, object> payload)
        {
            if (!string.IsNullOrEmpty(AbTest))          payload["abTest"]          = AbTest;
            if (!string.IsNullOrEmpty(AbGroup))         payload["abGroup"]         = AbGroup;
            if (!string.IsNullOrEmpty(AbTestStartDate)) payload["abTestStartDate"] = AbTestStartDate;
            return payload;
        }

        protected void MergeCustomFields(Dictionary<string, object> payload, AnalyticsEventData data)
        {
            MergeCustomFields(payload, data.CustomFields);
        }

        protected void MergeCustomFields(Dictionary<string, object> payload, Dictionary<string, object>? customFields)
        {
            if (customFields == null) return;
            foreach (var kvp in customFields)
                payload[kvp.Key] = kvp.Value;
        }

        private Dictionary<string, object> WithBaseFields(Dictionary<string, object>? payload)
        {
            var result = new Dictionary<string, object>
            {
                { "adid",      adId },
                { "appToken", appToken },
            };
            if (payload != null)
                foreach (var kvp in payload)
                    result[kvp.Key] = kvp.Value;
            return WithAbTestFields(result);
        }

        #region metica-events
        public virtual void LogPurchaseEvent(string productId, string currency, double amount, string status, string? errorCode, string? referenceId, Dictionary<string, object>? customPayload)
        {
#if METICA_ANALYTICS
            var mergedPayload = WithBaseFields(customPayload);
            MeticaSdk.Analytics.LogPurchaseEvent(productId, currency, amount, status, errorCode, referenceId, mergedPayload);
            Debug.Log($"[MeticaAnalytics] LogPurchaseEvent: {productId}, {currency}, {amount}, {status}\nPayload: {JsonConvert.SerializeObject(mergedPayload)}");
#endif
        }

        public virtual void LogSessionStartEvent(Dictionary<string, object>? customPayload)
        {
#if METICA_ANALYTICS
            var mergedPayload = WithBaseFields(customPayload);
            MeticaSdk.Analytics.LogSessionStartEvent(mergedPayload);
            Debug.Log($"[MeticaAnalytics] LogSessionStartEvent\nPayload: {JsonConvert.SerializeObject(mergedPayload)}");
#endif
        }

        public virtual void LogInstallEvent(Dictionary<string, object>? customPayload)
        {
#if METICA_ANALYTICS
            var mergedPayload = WithBaseFields(customPayload);
            MeticaSdk.Analytics.LogInstallEvent(mergedPayload);
            Debug.Log($"[MeticaAnalytics] LogInstallEvent\nPayload: {JsonConvert.SerializeObject(mergedPayload)}");
#endif
        }

        public virtual void LogImpressionEvent(double value, string type, string mediator, string source, string? placement, Dictionary<string, object>? customPayload)
        {
#if METICA_ANALYTICS
            var mergedPayload = WithBaseFields(customPayload);
            MeticaSdk.Analytics.LogImpressionEvent(value, type, mediator, source, placement, mergedPayload);
            Debug.Log($"[MeticaAnalytics] LogImpressionEvent: {value}, {type}, {mediator}, {source}\nPayload: {JsonConvert.SerializeObject(mergedPayload)}");
#endif
        }

        public virtual void LogFullStateUpdateEvent(Dictionary<string, object> attributes)
        {
#if METICA_ANALYTICS
            var mergedAttributes = WithBaseFields(attributes);
            MeticaSdk.Analytics.LogFullStateUpdateEvent(mergedAttributes);
            Debug.Log($"[MeticaAnalytics] LogFullStateUpdateEvent: {mergedAttributes.Count} attributes\nAttributes: {JsonConvert.SerializeObject(mergedAttributes)}");
#endif
        }

        public virtual void LogPartialStateUpdateEvent(Dictionary<string, object> attributes)
        {
#if METICA_ANALYTICS
            var mergedAttributes = WithBaseFields(attributes);
            MeticaSdk.Analytics.LogPartialStateUpdateEvent(mergedAttributes);
            Debug.Log($"[MeticaAnalytics] LogPartialStateUpdateEvent: {mergedAttributes.Count} attributes\nAttributes: {JsonConvert.SerializeObject(mergedAttributes)}");
#endif
        }

        public virtual void LogCustomEvent(string eventName, Dictionary<string, object>? properties)
        {
#if METICA_ANALYTICS
            var mergedProperties = WithBaseFields(properties);
            MeticaSdk.Analytics.LogCustomEvent(eventName, mergedProperties);
            Debug.Log($"[MeticaAnalytics] LogCustomEvent: {eventName}\nProperties: {JsonConvert.SerializeObject(mergedProperties)}");
#endif
        }

        /// <summary>
        /// Logs the "perfStats" custom event. When GD Performance Tracker is installed, fetches
        /// its perf payload (FPS/memory since the last call) and skips logging if tracking is off
        /// or nothing was recorded; otherwise logs <paramref name="customPayload"/> as-is.
        /// <paramref name="customPayload"/> always adds game-context fields (e.g. taskId, day) on top.
        /// </summary>
        public virtual void LogPerfStatsEvent(Dictionary<string, object>? customPayload = null)
        {
#if METICA_ANALYTICS
            Dictionary<string, object>? perfPayload;
#if GD_PERFORMANCE_TRACKER
            perfPayload = GDPerformance.ConsumePerfPayload();
            if (perfPayload == null) return;
            MergeCustomFields(perfPayload, customPayload);
#else
            perfPayload = customPayload;
#endif
            var mergedPayload = WithBaseFields(perfPayload);
            MeticaSdk.Analytics.LogCustomEvent("perfStats", mergedPayload);
            Debug.Log($"[MeticaAnalytics] LogPerfStatsEvent\nPayload: {JsonConvert.SerializeObject(mergedPayload)}");
#endif
        }

        /// <summary>
        /// Logs the "loadTime" custom event. When GD Performance Tracker is installed, fetches its
        /// cold-start payload and skips logging if startup tracking is off; otherwise logs
        /// <paramref name="customPayload"/> as-is. <paramref name="customPayload"/> always adds
        /// game-context fields on top.
        /// </summary>
        public virtual void LogLoadTimeEvent(Dictionary<string, object>? customPayload = null)
        {
#if METICA_ANALYTICS
            Dictionary<string, object>? startupPayload;
#if GD_PERFORMANCE_TRACKER
            startupPayload = GDPerformance.GetStartupPayload();
            if (startupPayload == null) return;
            MergeCustomFields(startupPayload, customPayload);
#else
            startupPayload = customPayload;
#endif
            var mergedPayload = WithBaseFields(startupPayload);
            MeticaSdk.Analytics.LogCustomEvent("loadTime", mergedPayload);
            Debug.Log($"[MeticaAnalytics] LogLoadTimeEvent\nPayload: {JsonConvert.SerializeObject(mergedPayload)}");
#endif
        }
        #endregion
    }
}