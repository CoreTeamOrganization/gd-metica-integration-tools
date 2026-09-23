using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The one Metica SDK version this tool installs and expects to find installed.
    ///
    /// <para>A project whose Metica SDK does not match this — older, newer, or missing —
    /// gets Assets/MeticaSdk removed and reinstalled. Bumping this asset's field is the
    /// only thing needed to move every project this tool touches onto a new SDK release,
    /// with no code change.</para>
    /// </summary>
    public sealed class MeticaTargetVersion : ScriptableObject
    {
        [SerializeField] private string version = "2.45.2";

        public string Version => version;
    }
}
