using System.IO;
using System.Linq;
using UnityEditor;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Opens the Genre Creator — where genre files are actually authored, manually or from
    /// an Excel schema.
    ///
    /// <para>Not a one-time setup step like the ones before it: a genre is content, not
    /// configuration, so this always verifies (there is no "wrong" number of genres) and
    /// stays reachable for as long as the game keeps adding them.</para>
    /// </summary>
    public sealed class GenreDefinitionStep : MeticaStep
    {
        public override string Title => "Create genres";

        public override bool Optional => true;

        public override string Summary =>
            "Opens the Genre Creator, where genre analytics files are actually authored — manually or " +
            "imported from an Excel schema. Come back here any time you add a new genre.";

        public override string ActionLabel => "Open Genre Creator";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var genres = CountGenres();
            result.Note(genres == 0
                ? "No genres created yet."
                : $"{genres} genre{(genres == 1 ? "" : "s")} created so far.");

            return result.Seal();
        }

        public override void Apply() => GenreCreatorWindow.ShowWindow();

        public override void DrawBody(VerifyResult result)
        {
            EditorGUILayout.HelpBox(
                "This is where genre files actually get authored. The button opens the Genre Creator " +
                "window — the same tool, any number of times, for as many genres as the game needs.",
                MessageType.Info);
        }

        private static int CountGenres()
        {
            var folder = MeticaPaths.ToAbsolute(MeticaPaths.GenresRoot);
            if (!Directory.Exists(folder)) return 0;

            return Directory.GetDirectories(folder).Count(d => Path.GetFileName(d) != "Resources");
        }
    }
}
