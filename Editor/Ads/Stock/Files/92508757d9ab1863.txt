using System;
using System.Collections;
using System.Collections.Generic;
using Monetization.Runtime.Utilities;
using UnityEngine;

namespace Monetization.Runtime.Utilities
{
    internal interface IDispatcherService : ITickable
    {
        void Enqueue(Action action);
    }
}
