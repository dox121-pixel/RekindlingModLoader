using System;
using System.Collections.Generic;
using System.IO;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Redirects the game's content loads to files supplied by mods.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asset names are the same strings the game passes to <c>Content.Load</c>, minus the
    /// <c>.xnb</c> extension - for example <c>Hud/RekinIcon</c> or <c>Awards/cheers</c>.
    /// Matching is case-insensitive and treats <c>/</c>, <c>//</c> and <c>\</c> as equivalent,
    /// because the game itself mixes all three.
    /// </para>
    /// <para>
    /// Both <c>.xnb</c> and loose <c>.png</c> files work. A <c>.png</c> is converted to an
    /// in-memory XNB on first load and cached, so authors never need the content pipeline.
    /// </para>
    /// </remarks>
    public interface IAssetOverrides
    {
        /// <summary>
        /// Redirects <paramref name="assetName"/> to a file on disk. The file may be a
        /// <c>.xnb</c> or a <c>.png</c>.
        /// </summary>
        /// <param name="assetName">Asset name as passed to <c>Content.Load</c>, e.g. <c>Hud/RekinIcon</c>.</param>
        /// <param name="filePath">Absolute path to the replacement file.</param>
        /// <exception cref="FileNotFoundException">The replacement file does not exist.</exception>
        void Override(string assetName, string filePath);

        /// <summary>
        /// Redirects <paramref name="assetName"/> to bytes produced on demand. The factory is
        /// called every time the game loads that asset, and must return raw XNB content.
        /// </summary>
        void Override(string assetName, Func<Stream> streamFactory);

        /// <summary>
        /// Registers every asset file under <paramref name="directory"/>, deriving asset names
        /// from the paths relative to it. A file at
        /// <c>&lt;directory&gt;/Hud/RekinIcon.png</c> overrides the asset <c>Hud/RekinIcon</c>.
        /// </summary>
        /// <returns>The number of assets registered.</returns>
        int OverrideDirectory(string directory);

        /// <summary>Removes an override, restoring the game's own asset.</summary>
        bool Remove(string assetName);

        /// <summary>True when something has claimed this asset name.</summary>
        bool IsOverridden(string assetName);

        /// <summary>
        /// Every currently registered asset name, mapped to the id of the mod that claimed it.
        /// Useful for diagnosing which mod won a conflict.
        /// </summary>
        IReadOnlyDictionary<string, string> Registered { get; }
    }
}
