using System.Collections;
using System.Collections.Generic;
using AdjustSdk;
using UnityEngine;

namespace Monetization.Runtime.Configurations
{
    [CreateAssetMenu(fileName = "AdjustConfig", menuName = "GDMonetization/AdjustConfig")]
    public sealed class AdjustConfiguration : ScriptableObject
    {
        public string AppToken;
        public AdjustEnvironment Environment = AdjustEnvironment.Sandbox;
        public AdjustLogLevel LogLevel = AdjustLogLevel.Info;
    }
}
