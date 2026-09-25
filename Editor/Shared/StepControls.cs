using System;

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

        public StepButton(string label, Action onClick, bool enabled = true, StepIcon icon = StepIcon.None)
        {
            Label = label;
            OnClick = onClick;
            Enabled = enabled;
            Icon = icon;
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
