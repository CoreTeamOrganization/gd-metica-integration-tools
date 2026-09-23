using System.IO;
using System.Linq;

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

        public override string Summary => "Author genres in the Genre Creator.";

        public override string Why =>
            "Genre analytics files are written in the Genre Creator window — by hand or imported from an " +
            "Excel schema. It's content, not setup, so come back here whenever you add a genre.";

        public override string ActionLabel => "Open Genre Creator";

        public override VerifyResult Verify()
        {
            var result = new VerifyResult();

            var genres = CountGenres();
            result.Note(genres == 0
                ? "No genres yet"
                : $"{genres} genre{(genres == 1 ? "" : "s")}");

            return result.Seal();
        }

        public override void Apply() => GenreCreatorWindow.ShowWindow();

        /// <summary>Generated genres: every folder under GenresRoot except its shared Resources.</summary>
        internal static int CountGenres()
        {
            var folder = MeticaPaths.ToAbsolute(MeticaPaths.GenresRoot);
            if (!Directory.Exists(folder)) return 0;

            return Directory.GetDirectories(folder).Count(d => Path.GetFileName(d) != "Resources");
        }
    }
}
