using System;
using System.Collections.Generic;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Something a step shows between its notes and its action row — an extra button, a
    /// switch, a dropdown. Steps only describe their controls; the window draws them in the
    /// tool's theme and runs their callbacks on the next editor tick, then re-verifies.
    /// </summary>
    internal abstract class StepControl
    {
    }

    internal enum StepIcon
    {
        None,
        Folder,
        File
    }

    internal sealed class StepButton : StepControl
    {
        public readonly string Label;
        public readonly Action OnClick;
        public readonly bool Enabled;
        public readonly StepIcon Icon;

        /// <summary>
        /// When set, the button only shows while this is true — re-checked as the step's text
        /// fields change, so it can appear and disappear while typing.
        /// </summary>
        public readonly Func<bool> ShowWhen;

        public StepButton(string label, Action onClick, bool enabled = true, StepIcon icon = StepIcon.None,
            Func<bool> showWhen = null)
        {
            Label = label;
            OnClick = onClick;
            Enabled = enabled;
            Icon = icon;
            ShowWhen = showWhen;
        }
    }

    /// <summary>An on/off switch.</summary>
    internal sealed class StepToggle : StepControl
    {
        public readonly string Label;
        public readonly bool Value;
        public readonly Action<bool> OnChanged;

        public StepToggle(string label, bool value, Action<bool> onChanged)
        {
            Label = label;
            Value = value;
            OnChanged = onChanged;
        }
    }

    /// <summary>
    /// A labelled text field. Unlike the other controls its callback runs on every keystroke
    /// and does not re-check the step — a rebuild would take the focus away mid-typing.
    /// </summary>
    internal sealed class StepText : StepControl
    {
        public readonly string Label;
        public readonly string Value;
        public readonly Action<string> OnChanged;

        /// <summary>Masked until the eye button is pressed — for API keys.</summary>
        public readonly bool Secret;

        public StepText(string label, string value, Action<string> onChanged, bool secret = false)
        {
            Label = label;
            Value = value;
            OnChanged = onChanged;
            Secret = secret;
        }
    }

    /// <summary>
    /// One collapsible row of a checklist: a title, a status line and its colour, and the
    /// controls that fix it, shown when the row is expanded.
    /// </summary>
    internal sealed class StepItem : StepControl
    {
        public readonly string Title;
        public readonly bool Done;
        public readonly string Status;
        public readonly IReadOnlyList<StepControl> Actions;

        /// <summary>
        /// Optional faded line under the title, such as a file's folder. With one, the status
        /// sits next to the title, smaller.
        /// </summary>
        public readonly string Detail;

        /// <summary>Optional small buttons shown while the pointer is over the row.</summary>
        public readonly IReadOnlyList<StepButton> HoverActions;

        /// <summary>What keeps the row open across rebuilds: unique even when titles repeat.</summary>
        public string Key => Detail == null ? Title : Detail + "/" + Title;

        public StepItem(string title, bool done, string status, IReadOnlyList<StepControl> actions,
            string detail = null, IReadOnlyList<StepButton> hoverActions = null)
        {
            Title = title;
            Done = done;
            Status = status;
            Actions = actions;
            Detail = detail;
            HoverActions = hoverActions ?? Array.Empty<StepButton>();
        }
    }

    /// <summary>
    /// One line of code in a list: its number, the code, and a hint under it. Click to open the
    /// file there; <see cref="HoverActions"/> show while the pointer is over it.
    /// </summary>
    internal sealed class StepLine : StepControl
    {
        public readonly int Line;
        public readonly string Code;
        public readonly string Hint;
        public readonly Action OnOpen;
        public readonly IReadOnlyList<StepButton> HoverActions;

        public StepLine(int line, string code, string hint, Action onOpen, IReadOnlyList<StepButton> hoverActions = null)
        {
            Line = line;
            Code = code;
            Hint = hint;
            OnOpen = onOpen;
            HoverActions = hoverActions ?? Array.Empty<StepButton>();
        }
    }

    /// <summary>A labelled dropdown, with an optional one-line caption under it.</summary>
    internal sealed class StepChoice : StepControl
    {
        public readonly string Label;
        public readonly string[] Options;
        public readonly int Selected;
        public readonly Action<int> OnChanged;
        public readonly string Caption;

        public StepChoice(string label, string[] options, int selected, Action<int> onChanged, string caption = null)
        {
            Label = label;
            Options = options;
            Selected = selected;
            OnChanged = onChanged;
            Caption = caption;
        }
    }
}
