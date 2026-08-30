using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Holds every mod-supplied asset replacement and resolves them at load time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Names are normalised aggressively before comparison because the game is inconsistent about
    /// separators - it loads <c>"Hud//RekinIcon"</c>, <c>"Sound/Music/NewDay"</c> and
    /// <c>"World Objects//Unknown"</c> in the same session. Normalisation also folds away the
    /// <c>Content</c> prefix, since the game uses two content managers with different root
    /// directories: <c>Game1.Content</c> has <c>RootDirectory = "Content"</c>, while
    /// <c>Game1.GlobalContent</c> has an empty root and prepends <c>"Content/"</c> to the asset
    /// name itself. Both must resolve to the same key.
    /// </para>
    /// <para>
    /// PNG conversion results are cached in memory. Textures are loaded repeatedly as areas
    /// stream in, and re-encoding a 2048x2048 sheet on every load would be visible as a stutter.
    /// </para>
    /// </remarks>
    internal sealed class AssetOverrideRegistry : IAssetOverrides
    {
        private readonly object _gate = new object();

        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _owners =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, byte[]> _conversionCache =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>Id of the mod currently being loaded, used to attribute registrations.</summary>
        internal string CurrentModId { get; set; } = "loader";

        private sealed class Entry
        {
            public string FilePath;
            public Func<Stream> Factory;
            public string OwnerId;
        }

        public IReadOnlyDictionary<string, string> Registered
        {
            get
            {
                lock (_gate)
                    return new Dictionary<string, string>(_owners, StringComparer.Ordinal);
            }
        }

        public void Override(string assetName, string filePath)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                throw new ArgumentException("Asset name must not be empty.", nameof(assetName));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path must not be empty.", nameof(filePath));

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Replacement asset not found: {fullPath}", fullPath);

            Register(assetName, new Entry { FilePath = fullPath, OwnerId = CurrentModId });
        }

        public void Override(string assetName, Func<Stream> streamFactory)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                throw new ArgumentException("Asset name must not be empty.", nameof(assetName));
            if (streamFactory == null)
                throw new ArgumentNullException(nameof(streamFactory));

            Register(assetName, new Entry { Factory = streamFactory, OwnerId = CurrentModId });
        }

        private void Register(string assetName, Entry entry)
        {
            string key = Normalize(assetName);

            lock (_gate)
            {
                if (_owners.TryGetValue(key, out string previousOwner) &&
                    !string.Equals(previousOwner, entry.OwnerId, StringComparison.OrdinalIgnoreCase))
                {
                    // Last writer wins, but say so loudly - a silently overridden texture is
                    // maddening to debug, and load order decides the winner.
                    Log.Warn("Assets",
                        $"'{assetName}' is overridden by both '{previousOwner}' and '{entry.OwnerId}'. " +
                        $"'{entry.OwnerId}' loaded later, so its version is used.");
                }

                _entries[key] = entry;
                _owners[key] = entry.OwnerId;
                _conversionCache.Remove(key);
            }
        }

        public int OverrideDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            string root = Path.GetFullPath(directory);
            int count = 0;

            foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                bool usable = extension.Equals(".xnb", StringComparison.OrdinalIgnoreCase)
                              || XnbEncoder.IsConvertibleImage(file);

                if (!usable)
                    continue;

                // "<root>/Hud/RekinIcon.png" -> asset "Hud/RekinIcon"
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string assetName = Path.ChangeExtension(relative, null);

                try
                {
                    Override(assetName, file);
                    count++;
                }
                catch (Exception ex)
                {
                    Log.Warn("Assets", $"Could not register '{relative}': {ex.Message}");
                }
            }

            return count;
        }

        public bool Remove(string assetName)
        {
            string key = Normalize(assetName);
            lock (_gate)
            {
                _owners.Remove(key);
                _conversionCache.Remove(key);
                return _entries.Remove(key);
            }
        }

        public bool IsOverridden(string assetName)
        {
            string key = Normalize(assetName);
            lock (_gate)
                return _entries.ContainsKey(key);
        }

        /// <summary>
        /// Returns a replacement stream for a content load, or <c>null</c> to let the game load
        /// its own file. Called from the <c>OpenStream</c> patch on every asset load.
        /// </summary>
        /// <param name="rootDirectory">The requesting content manager's root directory.</param>
        /// <param name="assetName">The asset name as passed to <c>Load</c>.</param>
        internal Stream TryOpen(string rootDirectory, string assetName)
        {
            string key = Normalize(Combine(rootDirectory, assetName));

            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out entry))
                    return null;

                if (_conversionCache.TryGetValue(key, out byte[] cached))
                    return new MemoryStream(cached, writable: false);
            }

            try
            {
                if (entry.Factory != null)
                    return entry.Factory();

                if (XnbEncoder.IsConvertibleImage(entry.FilePath))
                {
                    byte[] converted = XnbEncoder.FromImageFile(entry.FilePath);

                    lock (_gate)
                        _conversionCache[key] = converted;

                    Log.Debug("Assets", $"Converted '{Path.GetFileName(entry.FilePath)}' for asset '{assetName}'.");
                    return new MemoryStream(converted, writable: false);
                }

                return File.OpenRead(entry.FilePath);
            }
            catch (Exception ex)
            {
                // Fall through to the original asset rather than crashing the content load.
                Log.Error("Assets",
                    $"Failed to supply override for '{assetName}' (from '{entry.OwnerId}'); " +
                    "using the game's own asset instead.", ex);
                return null;
            }
        }

        private static string Combine(string rootDirectory, string assetName)
            => string.IsNullOrEmpty(rootDirectory) ? assetName : rootDirectory + "/" + assetName;

        /// <summary>
        /// Folds separator style, casing, the <c>Content</c> prefix and the <c>.xnb</c> suffix
        /// into a single canonical key.
        /// </summary>
        internal static string Normalize(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                return string.Empty;

            var sb = new StringBuilder(assetName.Length);
            bool lastWasSeparator = false;

            foreach (char raw in assetName.Trim())
            {
                char c = (raw == '\\') ? '/' : raw;

                if (c == '/')
                {
                    // Collapse runs of separators; the game writes "Hud//RekinIcon".
                    if (lastWasSeparator || sb.Length == 0)
                        continue;
                    lastWasSeparator = true;
                    sb.Append('/');
                    continue;
                }

                lastWasSeparator = false;
                sb.Append(char.ToLowerInvariant(c));
            }

            // Drop a trailing separator.
            while (sb.Length > 0 && sb[sb.Length - 1] == '/')
                sb.Length--;

            string result = sb.ToString();

            // "./Hud/Icon" -> "Hud/Icon"
            while (result.StartsWith("./", StringComparison.Ordinal))
                result = result.Substring(2);

            // Both content managers must land on the same key.
            if (result.StartsWith("content/", StringComparison.Ordinal))
                result = result.Substring("content/".Length);

            if (result.EndsWith(".xnb", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 4);

            return result;
        }
    }
}
