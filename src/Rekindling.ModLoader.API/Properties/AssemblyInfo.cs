using System.Runtime.CompilerServices;

// The loader raises the events declared in this assembly; mods only subscribe to them.
[assembly: InternalsVisibleTo("Rekindling.ModLoader")]

// The test suite exercises the event raisers directly.
[assembly: InternalsVisibleTo("Rekindling.ModLoader.Tests")]
