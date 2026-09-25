using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Genre Creator's setup: the Android toolchain Metica's native SDK needs (the Gradle
    /// steps are shared with Ads Integration, so one place owns baseProjectTemplate.gradle),
    /// the optional Performance Tracker package, and the genre-authoring window itself.
    /// </summary>
    internal static class GenreFlow
    {
        public const string Name = "Genre Creator";

        private static readonly MeticaStep[] Steps =
        {
            new GradleVersionStep(),
            new GradleJdkStep(),
            new KotlinTemplateStep(),
            new PerformanceTrackerStep(),
            new GenreDefinitionStep()
        };

        public static MeticaFlow Create() =>
            new MeticaFlow(Name, () => Steps, () => Steps.Select(s => s.Id));
    }
}
