using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monetization.Runtime.Analytics
{
    public interface IAnalyticsEventsService
    {
        void SendEvent(string name);
        void SendEvent(string name, Dictionary<string, object> parameters);
    }
}
