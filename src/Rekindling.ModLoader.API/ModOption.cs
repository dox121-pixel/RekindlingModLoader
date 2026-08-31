using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rekindling.ModLoader
{
    /// <summary>What kind of control an option wants, so a UI can render it generically.</summary>
    public enum ModOptionKind
    {
        Toggle,
        Choice,
        Slider,
        Point,

        /// <summary>Free text. Usually hidden, for state a mod persists but does not expose.</summary>
        Text,

        /// <summary>Not a value at all - a button that runs something.</summary>
        Action
    }

    /// <summary>
    /// One setting belonging to a mod.
    /// </summary>
    /// <remarks>
    /// Options are declared by the mod that owns them and stored by the loader, but rendered by
    /// whichever mod provides the settings UI. That split is deliberate: a mod should not have to
    /// draw anything to be configurable, and the game should not end up with five different
    /// options screens.
    /// </remarks>
    public abstract class ModOption
    {
        protected ModOption(string key, string label, string description)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("An option needs a key.", nameof(key));

            Key = key;
            Label = string.IsNullOrWhiteSpace(label) ? key : label;
            Description = description;
        }

        /// <summary>Stable identifier, used as the storage key. Changing it loses saved values.</summary>
        public string Key { get; }

        /// <summary>Shown next to the control.</summary>
        public string Label { get; }

        /// <summary>Optional one-liner explaining what it does.</summary>
        public string Description { get; }

        /// <summary>
        /// When true a settings UI should skip this option.
        /// </summary>
        /// <remarks>
        /// For state a mod needs persisted but which has no sensible generic control - a list of
        /// filenames, a remembered window position. The mod presents it its own way, if at all.
        /// </remarks>
        public bool Hidden { get; set; }

        public abstract ModOptionKind Kind { get; }

        /// <summary>True when the value has not been changed from its default.</summary>
        public abstract bool IsDefault { get; }

        /// <summary>Restores the default value.</summary>
        public abstract void Reset();

        /// <summary>Raised whenever the value changes, however it changed.</summary>
        public event Action<ModOption> Changed;

        protected void RaiseChanged() => Changed?.Invoke(this);

        /// <summary>Serialised form. The loader persists this; UIs should not need it.</summary>
        public abstract string Serialize();

        /// <summary>Restores from <see cref="Serialize"/>. Bad input leaves the value alone.</summary>
        public abstract void Deserialize(string raw);
    }

    /// <summary>An on/off setting.</summary>
    public sealed class ToggleOption : ModOption
    {
        private bool _value;

        public ToggleOption(string key, string label, bool defaultValue, string description = null)
            : base(key, label, description)
        {
            DefaultValue = defaultValue;
            _value = defaultValue;
        }

        public bool DefaultValue { get; }

        public bool Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return;

                _value = value;
                RaiseChanged();
            }
        }

        public override ModOptionKind Kind => ModOptionKind.Toggle;
        public override bool IsDefault => _value == DefaultValue;
        public override void Reset() => Value = DefaultValue;

        public override string Serialize() => _value ? "true" : "false";

        public override void Deserialize(string raw)
        {
            if (bool.TryParse(raw, out bool parsed))
                Value = parsed;
        }

        public void Toggle() => Value = !Value;
    }

    /// <summary>A pick-one-from-a-list setting.</summary>
    public sealed class ChoiceOption : ModOption
    {
        private string _value;

        private readonly List<string> _choices;

        public ChoiceOption(string key, string label, IEnumerable<string> choices, string defaultValue, string description = null)
            : base(key, label, description)
        {
            _choices = new List<string>(choices ?? new string[0]);

            if (_choices.Count == 0)
                throw new ArgumentException("A choice option needs at least one choice.", nameof(choices));

            DefaultValue = _choices.Contains(defaultValue) ? defaultValue : _choices[0];
            _value = DefaultValue;
        }

        public IReadOnlyList<string> Choices => _choices;

        public string DefaultValue { get; }

        public string Value
        {
            get => _value;
            set
            {
                // Ignore anything not on the list, so a stale config cannot put the option into
                // a state the mod never anticipated.
                if (value == null || !_choices.Contains(value) || _value == value)
                    return;

                _value = value;
                RaiseChanged();
            }
        }

        /// <summary>Index of the current value within <see cref="Choices"/>.</summary>
        public int SelectedIndex => _choices.IndexOf(_value);

        /// <summary>Moves to the next choice, wrapping. What a click on the control usually does.</summary>
        public void Next()
        {
            int next = (SelectedIndex + 1) % _choices.Count;
            Value = _choices[next];
        }

        public void Previous()
        {
            int previous = (SelectedIndex - 1 + _choices.Count) % _choices.Count;
            Value = _choices[previous];
        }

        public override ModOptionKind Kind => ModOptionKind.Choice;
        public override bool IsDefault => _value == DefaultValue;
        public override void Reset() => Value = DefaultValue;

        public override string Serialize() => _value;

        public override void Deserialize(string raw) => Value = raw;
    }

    /// <summary>A number within a range.</summary>
    public sealed class SliderOption : ModOption
    {
        private float _value;

        public SliderOption(string key, string label, float minimum, float maximum, float defaultValue,
            float step = 1f, string description = null)
            : base(key, label, description)
        {
            if (maximum <= minimum)
                throw new ArgumentException("maximum must be greater than minimum.", nameof(maximum));

            Minimum = minimum;
            Maximum = maximum;
            Step = step > 0f ? step : 1f;
            DefaultValue = Clamp(defaultValue);
            _value = DefaultValue;
        }

        public float Minimum { get; }
        public float Maximum { get; }
        public float Step { get; }
        public float DefaultValue { get; }

        public float Value
        {
            get => _value;
            set
            {
                float clamped = Clamp(value);
                if (Math.Abs(clamped - _value) < 0.0001f)
                    return;

                _value = clamped;
                RaiseChanged();
            }
        }

        /// <summary>Where the value sits in its range, 0 to 1. Handy for drawing a bar.</summary>
        public float Normalized => (_value - Minimum) / (Maximum - Minimum);

        private float Clamp(float value)
        {
            if (value < Minimum) return Minimum;
            if (value > Maximum) return Maximum;
            return value;
        }

        public override ModOptionKind Kind => ModOptionKind.Slider;
        public override bool IsDefault => Math.Abs(_value - DefaultValue) < 0.0001f;
        public override void Reset() => Value = DefaultValue;

        public override string Serialize() => _value.ToString("R", CultureInfo.InvariantCulture);

        public override void Deserialize(string raw)
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                Value = parsed;
        }
    }

    /// <summary>
    /// A position on screen, stored as a fraction of the screen rather than pixels so it survives
    /// a resolution change.
    /// </summary>
    /// <remarks>
    /// Meant to be set by dragging something around rather than typed, so a UI should offer a
    /// placement mode rather than two number boxes.
    /// </remarks>
    public sealed class PointOption : ModOption
    {
        private float _x;
        private float _y;

        public PointOption(string key, string label, float defaultX, float defaultY, string description = null)
            : base(key, label, description)
        {
            DefaultX = Clamp(defaultX);
            DefaultY = Clamp(defaultY);
            _x = DefaultX;
            _y = DefaultY;
        }

        public float DefaultX { get; }
        public float DefaultY { get; }

        /// <summary>Horizontal position, 0 (left edge) to 1 (right edge).</summary>
        public float X => _x;

        /// <summary>Vertical position, 0 (top) to 1 (bottom).</summary>
        public float Y => _y;

        public void Set(float x, float y)
        {
            float clampedX = Clamp(x);
            float clampedY = Clamp(y);

            if (Math.Abs(clampedX - _x) < 0.0001f && Math.Abs(clampedY - _y) < 0.0001f)
                return;

            _x = clampedX;
            _y = clampedY;
            RaiseChanged();
        }

        private static float Clamp(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        public override ModOptionKind Kind => ModOptionKind.Point;

        public override bool IsDefault
            => Math.Abs(_x - DefaultX) < 0.0001f && Math.Abs(_y - DefaultY) < 0.0001f;

        public override void Reset() => Set(DefaultX, DefaultY);

        public override string Serialize()
            => _x.ToString("R", CultureInfo.InvariantCulture) + "," + _y.ToString("R", CultureInfo.InvariantCulture);

        public override void Deserialize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            string[] parts = raw.Split(',');
            if (parts.Length != 2)
                return;

            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                Set(x, y);
            }
        }
    }
}

namespace Rekindling.ModLoader
{
    /// <summary>
    /// A free-text setting. Usually <see cref="ModOption.Hidden"/>, holding state a mod persists
    /// but presents through its own UI rather than a generic text box.
    /// </summary>
    public sealed class TextOption : ModOption
    {
        private string _value;

        public TextOption(string key, string label, string defaultValue, string description = null)
            : base(key, label, description)
        {
            DefaultValue = defaultValue ?? string.Empty;
            _value = DefaultValue;
        }

        public string DefaultValue { get; }

        public string Value
        {
            get => _value;
            set
            {
                string incoming = value ?? string.Empty;
                if (string.Equals(_value, incoming, System.StringComparison.Ordinal))
                    return;

                _value = incoming;
                RaiseChanged();
            }
        }

        public override ModOptionKind Kind => ModOptionKind.Text;
        public override bool IsDefault => string.Equals(_value, DefaultValue, System.StringComparison.Ordinal);
        public override void Reset() => Value = DefaultValue;

        public override string Serialize() => _value;
        public override void Deserialize(string raw) => Value = raw;
    }

    /// <summary>
    /// A button in a settings screen. Holds no value and is never persisted; activating it just
    /// runs the mod's callback, which is how a mod offers something a generic control cannot
    /// express - opening a folder, running a scan, resetting saved data.
    /// </summary>
    public sealed class ActionOption : ModOption
    {
        private readonly System.Action _action;

        public ActionOption(string key, string label, string buttonText, System.Action action, string description = null)
            : base(key, label, description)
        {
            ButtonText = string.IsNullOrWhiteSpace(buttonText) ? "Go" : buttonText;
            _action = action;
        }

        /// <summary>Text shown on the button itself.</summary>
        public string ButtonText { get; }

        public void Invoke() => _action?.Invoke();

        public override ModOptionKind Kind => ModOptionKind.Action;

        // Nothing to store, so it is always "default" and serialising is a no-op.
        public override bool IsDefault => true;
        public override void Reset() { }
        public override string Serialize() => string.Empty;
        public override void Deserialize(string raw) { }
    }
}
