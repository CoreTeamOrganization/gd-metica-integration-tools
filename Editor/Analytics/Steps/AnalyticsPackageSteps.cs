using System.Collections.Generic;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Step 1 of Analytics: the Metica SDK, through the Ads flow's own step — same target
    /// version, old Metica removed first, then Android libraries resolved (the step only passes
    /// once they are). Its own step here so it is reviewed and committed on its own.
    /// </summary>
    public sealed class AddMeticaSdkStep : MeticaStep
    {
        private readonly ImportMeticaSdkStep _metica = new ImportMeticaSdkStep();

        public override string Title =>
            TargetVersions.MeticaSdk is string target ? $"Add Metica v{target}" : "Add Metica SDK";

        public override string Summary => _metica.Summary;

        public override string Why => _metica.Why;

        /// <summary>The Ads step's action; a fresh install reads like the title.</summary>
        public override string ActionLabel
        {
            get
            {
                var action = _metica.ActionLabel;
                return action != null && action.StartsWith("Download and import") ? Title : action;
            }
        }

        public override IEnumerable<string> TouchedPaths => _metica.TouchedPaths;

        public override string ReviewHint => _metica.ReviewHint;

        public override VerifyResult Verify() => _metica.Verify();

        public override void Apply() => _metica.Apply();

        internal override IEnumerable<StepControl> Controls(VerifyResult result) => _metica.Controls(result);
    }

    /// <summary>
    /// Step 2 of Analytics: com.metica.analytics.abstractions. Not on any registry — a .tgz kept
    /// in the project's Packages folder and referenced by a file: dependency.
    ///
    /// <para>Before the genre creator on purpose: it turns METICA_ANALYTICS on as soon as the
    /// Metica SDK is in the project, and with that define the Metica SDK only compiles with the
    /// abstractions there.</para>
    /// </summary>
    public sealed class AddAnalyticsAbstractionsStep : MeticaStep
    {
        public override string Title => "Add Metica Analytics Abstractions";

        public override string Summary => "Add the analytics interfaces the Metica SDK compiles against.";

        public override string Why =>
            $"Downloads {AnalyticsAbstractions.FileName} into Packages/ and adds " +
            $"\"{AnalyticsAbstractions.PackageName}\": \"file:{AnalyticsAbstractions.FileName}\" to manifest.json. " +
            "It has to be in before the Genre Creator, which turns METICA_ANALYTICS on. Pick .tgz instead uses a " +
            "local copy. Skipped when something else already provides the assembly (GD SDK 6.2.2+ carries its own).";

        public override string ActionLabel => AnalyticsAbstractions.Present ? null : Title;

        public override IEnumerable<string> TouchedPaths => new[]
        {
            AnalyticsAbstractions.ProjectFile,
            MeticaPaths.PackagesManifest,
            "Packages/packages-lock.json"
        };

        public override string ReviewHint => "The .tgz in Packages/ and one new line in manifest.json.";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            if (!AnalyticsAbstractions.Present) return result.Problem("Not added yet.").Seal();
            if (!AnalyticsAbstractions.IsLoaded) return result.Wait("Package Manager is adding it…").Seal();
            return result.Note("Installed").Seal();
        }

        public override void Apply() => AnalyticsAbstractions.Install(Title);

        internal override IEnumerable<StepControl> Controls(VerifyResult result)
        {
            if (AnalyticsAbstractions.Present) yield break;

            // The download link is the usual way; a local copy works the same.
            yield return new StepButton("Pick .tgz instead…", () => AnalyticsAbstractions.InstallFromPickedFile(Title),
                icon: StepIcon.File);
        }
    }

    /// <summary>
    /// Step 3 of Analytics: the genre creator package, which writes the game's genre files from
    /// its Excel schema. An embedded copy in Packages/ counts too.
    /// </summary>
    public sealed class AddGenreCreatorStep : MeticaStep
    {
        public override string Title => $"Add Genre Creator v{GenreCreatorPackage.Version}";

        public override string Summary => "Add the package that makes the genre files from the Excel schema.";

        public override string Why =>
            $"Adds {GenreCreatorPackage.GitUrl} through Package Manager (Unity needs git on the PATH and access to the " +
            "CoreTeamOrganization GitHub org). A copy already embedded in Packages/ counts. The tool hands the game " +
            "name and the Excel file to it in the next step.";

        public override string ActionLabel => GenreCreatorPackage.Present ? null : Title;

        /// <summary>
        /// ProjectSettings.asset too: once installed, the genre creator adds the METICA_ANALYTICS
        /// scripting define for Android and iOS.
        /// </summary>
        public override IEnumerable<string> TouchedPaths => new[]
        {
            MeticaPaths.PackagesManifest,
            "Packages/packages-lock.json",
            MeticaAnalyticsDefine.ProjectSettings
        };

        public override string ReviewHint =>
            "One new line in manifest.json and its packages-lock.json entry; METICA_ANALYTICS added to the " +
            "scripting defines in ProjectSettings.asset (any other Project Settings change shows there too).";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();
            if (!AnalyticsAbstractions.IsLoaded)
                return result.Problem("Add Metica Analytics Abstractions first.").Seal();

            if (!GenreCreatorPackage.Present) return result.Problem("Not added yet.").Seal();
            if (!GenreCreatorPackage.IsLoaded) return result.Wait("Package Manager is adding it…").Seal();

            result.Note($"Installed {GenreCreatorPackage.InstalledVersion ?? "(version unknown)"}");
            var apiProblem = GenreCreatorPackage.ApiProblem;
            if (apiProblem != null) result.Problem(apiProblem);

            // The genre creator adds the define on the editor load after it arrives.
            if (!MeticaAnalyticsDefine.HasAndroid) result.Wait("Waiting for the genre creator to add METICA_ANALYTICS…");
            else result.Note("METICA_ANALYTICS");

            return result.Seal();
        }

        public override void Apply() => GenreCreatorPackage.Install(Title);
    }

    /// <summary>The METICA_ANALYTICS scripting define, which the genre creator adds.</summary>
    internal static class MeticaAnalyticsDefine
    {
        public const string Symbol = "METICA_ANALYTICS";
        public const string ProjectSettings = "ProjectSettings/ProjectSettings.asset";

        public static bool HasAndroid
        {
            get
            {
                UnityEditor.PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android, out var symbols);
                return System.Array.IndexOf(symbols, Symbol) >= 0;
            }
        }
    }
}
