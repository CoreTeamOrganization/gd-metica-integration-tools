using System;
using System.Collections.Generic;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Outcome of a single step's verification. A step is only considered done when
    /// <see cref="Ok"/> is true — the tool never trusts stored progress, it re-verifies
    /// the project on every repaint, so the wizard is safe to close and reopen.
    /// </summary>
    public sealed class VerifyResult
    {
        public bool Ok;

        /// <summary>Blocking reasons. Non-empty means the step has not passed.</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>Non-blocking remarks worth showing (versions found, things skipped).</summary>
        public readonly List<string> Notes = new List<string>();

        public static VerifyResult Pass(params string[] notes)
        {
            var r = new VerifyResult { Ok = true };
            if (notes != null) r.Notes.AddRange(notes);
            return r;
        }

        public static VerifyResult Fail(params string[] problems)
        {
            var r = new VerifyResult { Ok = false };
            if (problems != null) r.Problems.AddRange(problems);
            return r;
        }

        public VerifyResult Problem(string message)
        {
            Problems.Add(message);
            Ok = false;
            return this;
        }

        public VerifyResult Note(string message)
        {
            Notes.Add(message);
            return this;
        }

        /// <summary>Seals the result: passes only when nothing blocking was recorded.</summary>
        public VerifyResult Seal()
        {
            Ok = Problems.Count == 0;
            return this;
        }
    }

    /// <summary>
    /// One step of the Metica integration. Steps run strictly in order: the window
    /// refuses to run a step until every step before it verifies.
    /// </summary>
    public abstract class MeticaStep
    {
        /// <summary>Stable identity for the review record. The type name is enough.</summary>
        public string Id => GetType().Name;

        public abstract string Title { get; }

        /// <summary>
        /// True for a step some projects do not need. An optional step can be skipped,
        /// which unlocks the next one without the step having to verify. Everything else
        /// about it is unchanged — it still verifies, and skipping is reversible.
        /// </summary>
        public virtual bool Optional => false;

        /// <summary>One or two sentences describing what this step is for.</summary>
        public abstract string Summary { get; }

        /// <summary>
        /// Label for the action button. Null means the step has no action of its own —
        /// it is a check the project must satisfy (or a manual step driven by
        /// <see cref="DrawBody"/>).
        /// </summary>
        public virtual string ActionLabel => null;

        /// <summary>Reads the project and reports whether this step is satisfied.</summary>
        public abstract VerifyResult Verify();

        /// <summary>Performs the step. Must be safe to run twice.</summary>
        public virtual void Apply() { }

        /// <summary>Extra controls drawn inside the step, above the action button.</summary>
        public virtual void DrawBody(VerifyResult result) { }

        /// <summary>
        /// The longer explanation — why the step exists, what it touches, when to skip it.
        /// Shown behind a "Why?" foldout that starts closed, so the step itself stays down to
        /// its summary, one problem line and its buttons. Null when there is nothing to add.
        /// </summary>
        public virtual string Why => null;

        /// <summary>
        /// Project-relative paths this step creates or edits, for the review panel to build a
        /// diff command from. Empty for steps that only read the project.
        /// </summary>
        public virtual IEnumerable<string> TouchedPaths => Array.Empty<string>();

        /// <summary>
        /// One line on what to look for when reviewing this step, shown above the diff
        /// command. Null when the step changes nothing.
        /// </summary>
        public virtual string ReviewHint => null;
    }
}
