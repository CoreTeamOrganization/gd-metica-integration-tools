using UnityEngine;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The versions this tool installs: the Metica SDK, and the AppLovin MAX plugin for
    /// projects whose MAX is older than Metica needs.
    ///
    /// <para>A project whose Metica SDK does not match — older, newer, or missing — gets
    /// Assets/MeticaSdk removed and reinstalled; a MAX below 8.1.0 is replaced with
    /// <see cref="MaxVersion"/>. Changing this asset's fields moves every project this tool
    /// touches onto new releases, with no code change.</para>
    /// </summary>
    public sealed class MeticaTargetVersion : ScriptableObject
    {
        [SerializeField] private string version = "2.45.2";

        [Tooltip("AppLovin MAX plugin version installed when a project's MAX is below 8.1.0.")]
        [SerializeField] private string maxVersion = "8.1.0";

        public string Version => version;

        public string MaxVersion => maxVersion;
    }
}
