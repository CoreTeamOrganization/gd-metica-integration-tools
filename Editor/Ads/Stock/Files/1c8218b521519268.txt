using System;
using System.Collections;
using System.Collections.Generic;
using Monetization.Runtime.Utilities;
using UnityEngine;

namespace Monetization.Runtime.Utilities
{
    public static class ThreadDispatcher
    {
        private static readonly IDispatcherService dispatcher = new CustomDispatcher();

        public static void Enqueue(Action action)
        {
            dispatcher.Enqueue(action);
        }

        internal static ITickable GetTickable()
        {
            return dispatcher;
        }
    }

}
