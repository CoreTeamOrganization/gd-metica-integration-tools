using System.Collections;
using System.Collections.Generic;
using Monetization.Runtime.Ads;
using UnityEngine;

namespace Monetization.Runtime.Analytics
{
    public interface IAnalyticsAdRevenueService
    {
        void ReportAdRevenue(AdRevenueInfo adRevenueInfo);
    }
}
