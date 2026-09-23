using System.Collections.Generic;

namespace GameDistrict.MeticaAnalytics
{
    public abstract class AnalyticsEventData
    {
        public Dictionary<string, object> CustomFields { get; set; }
    }
}