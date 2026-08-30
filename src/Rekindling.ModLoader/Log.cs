using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Process-wide log sink. Writes to the console and to <c>Logs/modloader.log</c> in the game
    /// folder.
    /// </summary>
    /// <remarks>
    /// Writes are buffered and flushed on a size threshold rather than per line, because the
    /// per-frame events mods can subscribe to make it easy to log thousands of lines a second.
    /// <see cref="Flush"/> runs at shutdown and after any error, so a crash still leaves a
    /// complete log behind.
    /// </remarks>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static readonly StringBuilder Buffer = new StringBuilder();
        private static string _logPath;
        private static bool _consoleAvailable = true;
        private static LogLevel _minimumLevel = LogLevel.Info;

        /// <summary>Lines buffered before an automatic flush.</summary>
        private const int FlushThreshold = 8 * 1024;

        public static void Initialize(string gameDirectory, LogLevel minimumLevel)
        {
            lock (Gate)
            {
                _minimumLevel = minimumLevel;

                try
                {
                    string logDirectory = Path.Combine(gameDirectory, "Logs");
                    Directory.CreateDirectory(logDirectory);
                    _logPath = Path.Combine(logDirectory, "modloader.log");

                    // Keep exactly one previous run for comparison; more than that is clutter.
                    if (File.Exists(_logPath))
                    {
                        string previous = Path.Combine(logDirectory, "modloader.previous.log");
                        File.Copy(_logPath, previous, overwrite: true);
                    }

                    File.WriteAllText(_logPath, string.Empty);
                }
                catch (Exception ex)
                {
                    _logPath = null;
                    WriteConsole($"[ModLoader] Could not open the log file: {ex.Message}");
                }
            }
        }

        public static void Trace(string tag, string message) => Write(LogLevel.Trace, tag, message);
        public static void Debug(string tag, string message) => Write(LogLevel.Debug, tag, message);
        public static void Info(string tag, string message) => Write(LogLevel.Info, tag, message);
        public static void Warn(string tag, string message) => Write(LogLevel.Warn, tag, message);
        public static void Error(string tag, string message) => Write(LogLevel.Error, tag, message);

        public static void Error(string tag, string message, Exception exception)
        {
            Write(LogLevel.Error, tag, message);
            if (exception != null)
                Write(LogLevel.Error, tag, Describe(exception));
            Flush();
        }

        public static void Write(LogLevel level, string tag, string message)
        {
            if (level < _minimumLevel)
                return;

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:HH:mm:ss.fff}] [{1,-5}] [{2}] {3}",
                DateTime.Now, Abbreviate(level), tag, message);

            lock (Gate)
            {
                WriteConsole(line);

                if (_logPath != null)
                {
                    Buffer.AppendLine(line);
                    if (Buffer.Length >= FlushThreshold)
                        FlushLocked();
                }
            }
        }

        /// <summary>Writes anything buffered to disk. Safe to call at any time.</summary>
        public static void Flush()
        {
            lock (Gate)
            {
                FlushLocked();
            }
        }

        private static void FlushLocked()
        {
            if (_logPath == null || Buffer.Length == 0)
                return;

            try
            {
                File.AppendAllText(_logPath, Buffer.ToString());
            }
            catch
            {
                // Disk full, file locked, folder gone - none of that should stop the game.
            }
            finally
            {
                Buffer.Clear();
            }
        }

        private static void WriteConsole(string line)
        {
            if (!_consoleAvailable)
                return;

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // No console attached (launched via Steam with a WinExe host); stop trying.
                _consoleAvailable = false;
            }
        }

        private static string Abbreviate(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO";
                case LogLevel.Warn: return "WARN";
                default: return "ERROR";
            }
        }

        /// <summary>
        /// Renders an exception with all inner exceptions, and expands loader failures to list
        /// which specific type failed - the single most common cause of a mod not loading.
        /// </summary>
        internal static string Describe(Exception exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine(exception.ToString());

            if (exception is ReflectionTypeLoadExceptionWrapper wrapper)
            {
                foreach (string detail in wrapper.Details)
                    sb.AppendLine("  -> " + detail);
            }

            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Carries the per-type failures out of a <see cref="System.Reflection.ReflectionTypeLoadException"/>,
    /// which otherwise reports only a useless "one or more types could not be loaded".
    /// </summary>
    internal sealed class ReflectionTypeLoadExceptionWrapper : Exception
    {
        public ReflectionTypeLoadExceptionWrapper(string message, Exception inner, IEnumerable<string> details)
            : base(message, inner)
        {
            Details = new List<string>(details);
        }

        public IReadOnlyList<string> Details { get; }
    }

    /// <summary>Per-mod logger; prefixes every line with the mod id.</summary>
    internal sealed class ModLogger : IModLogger
    {
        private readonly string _tag;

        public ModLogger(string tag) => _tag = tag;

        public void Trace(string message) => Log.Trace(_tag, message);
        public void Debug(string message) => Log.Debug(_tag, message);
        public void Info(string message) => Log.Info(_tag, message);
        public void Warn(string message) => Log.Warn(_tag, message);
        public void Error(string message) => Log.Error(_tag, message);
        public void Error(string message, Exception exception) => Log.Error(_tag, message, exception);
        public void Write(LogLevel level, string message) => Log.Write(level, _tag, message);
    }
}
