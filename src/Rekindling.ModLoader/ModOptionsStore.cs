using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// A mod's options, backed by a JSON file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Values live in <c>ModConfig/&lt;mod id&gt;.json</c> in the game folder, not in the mod's own
    /// folder. Settings then survive updating or reinstalling a mod, which is what a player
    /// expects, and a mod folder stays something you can delete and replace wholesale.
    /// </para>
    /// <para>
    /// Saving is deferred rather than immediate. Dragging a slider or a UI element fires a change
    /// per frame, and writing the file each time would hammer the disk; instead a change marks
    /// the store dirty and the loader flushes it a moment later.
    /// </para>
    /// </remarks>
    internal sealed class ModOptionsStore : IModOptions
    {
        /// <summary>How long after the last change the file is written.</summary>
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

        private readonly List<ModOption> _options = new List<ModOption>();
        private readonly Dictionary<string, string> _saved =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly string _path;
        private readonly IModLogger _log;

        private DateTime _dirtySince = DateTime.MinValue;
        private bool _dirty;

        public ModOptionsStore(string modId, string configDirectory, IModLogger log)
        {
            ModId = modId;
            _log = log;
            _path = Path.Combine(configDirectory, SanitiseFileName(modId) + ".json");

            Load();
        }

        public string ModId { get; }

        public IReadOnlyList<ModOption> All => _options.ToArray();

        public event Action<ModOption> Changed;

        // ------------------------------------------------------------------ declaring

        public ToggleOption Toggle(string key, string label, bool defaultValue, string description = null)
            => Declare(new ToggleOption(key, label, defaultValue, description));

        public ChoiceOption Choice(string key, string label, IEnumerable<string> choices, string defaultValue, string description = null)
            => Declare(new ChoiceOption(key, label, choices, defaultValue, description));

        public SliderOption Slider(string key, string label, float minimum, float maximum, float defaultValue,
            float step = 1f, string description = null)
            => Declare(new SliderOption(key, label, minimum, maximum, defaultValue, step, description));

        public PointOption Point(string key, string label, float defaultX, float defaultY, string description = null)
            => Declare(new PointOption(key, label, defaultX, defaultY, description));

        private T Declare<T>(T option) where T : ModOption
        {
            if (Find(option.Key) != null)
                throw new InvalidOperationException($"Mod '{ModId}' declared the option '{option.Key}' twice.");

            // Restore the saved value as the option is declared, so the mod sees the player's
            // setting immediately rather than the default followed by a change event.
            if (_saved.TryGetValue(option.Key, out string stored))
            {
                try
                {
                    option.Deserialize(stored);
                }
                catch (Exception ex)
                {
                    _log?.Warn($"Could not restore option '{option.Key}' from the saved value '{stored}': {ex.Message}");
                }
            }

            option.Changed += OnOptionChanged;
            _options.Add(option);
            return option;
        }

        public ModOption Find(string key)
        {
            foreach (ModOption option in _options)
            {
                if (string.Equals(option.Key, key, StringComparison.Ordinal))
                    return option;
            }

            return null;
        }

        public void ResetAll()
        {
            foreach (ModOption option in _options)
                option.Reset();
        }

        private void OnOptionChanged(ModOption option)
        {
            _dirty = true;
            _dirtySince = DateTime.UtcNow;

            Changed?.Invoke(option);
        }

        // ---------------------------------------------------------------- persistence

        /// <summary>Writes the file if anything has changed and the write delay has elapsed.</summary>
        internal void FlushIfDue()
        {
            if (!_dirty || DateTime.UtcNow - _dirtySince < SaveDelay)
                return;

            Save();
        }

        public void Save()
        {
            _dirty = false;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");

                var json = new StringBuilder();
                json.AppendLine("{");
                json.AppendLine($"  \"_mod\": {Quote(ModId)},");

                for (int i = 0; i < _options.Count; i++)
                {
                    ModOption option = _options[i];
                    bool last = i == _options.Count - 1;

                    json.Append("  ")
                        .Append(Quote(option.Key))
                        .Append(": ")
                        .Append(Quote(option.Serialize()))
                        .AppendLine(last ? string.Empty : ",");
                }

                json.AppendLine("}");

                // Write to a temporary file and move it into place, so an interrupted write
                // cannot leave a truncated config that fails to parse next launch.
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, json.ToString(), Encoding.UTF8);

                if (File.Exists(_path))
                    File.Delete(_path);

                File.Move(temporary, _path);
            }
            catch (Exception ex)
            {
                _log?.Error($"Could not save options to '{_path}'.", ex);
            }
        }

        private void Load()
        {
            if (!File.Exists(_path))
                return;

            try
            {
                JsonValue json = JsonValue.Parse(File.ReadAllText(_path));

                foreach (KeyValuePair<string, JsonValue> entry in json.AsObject)
                {
                    if (entry.Key.StartsWith("_", StringComparison.Ordinal))
                        continue;

                    string value = entry.Value.AsString();
                    if (value != null)
                        _saved[entry.Key] = value;
                }
            }
            catch (JsonException ex)
            {
                // A corrupt config should not stop the mod loading; defaults are a fine fallback.
                _log?.Warn($"Options file '{Path.GetFileName(_path)}' is malformed ({ex.Message}); using defaults.");
            }
            catch (Exception ex)
            {
                _log?.Warn($"Could not read options from '{_path}': {ex.Message}");
            }
        }

        private static string Quote(string value)
        {
            var sb = new StringBuilder(value?.Length + 2 ?? 2);
            sb.Append('"');

            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static string SanitiseFileName(string value)
        {
            var sb = new StringBuilder(value.Length);

            foreach (char c in value)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);

            return sb.ToString();
        }
    }
}
