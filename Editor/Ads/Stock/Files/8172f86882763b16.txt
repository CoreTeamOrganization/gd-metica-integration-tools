using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monetization.Runtime.Analytics
{
    public interface IAnalyticsInitialization
    {
        void Initialize();
        bool IsInitialized { get; }
    }
}
