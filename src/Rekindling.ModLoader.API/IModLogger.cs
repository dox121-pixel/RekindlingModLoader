namespace Rekindling.ModLoader
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// Per-mod logger. Every line is tagged with the mod id and written to both the console
    /// and <c>Logs/modloader.log</c> in the game folder.
    /// </summary>
    public interface IModLogger
    {
        void Trace(string message);
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);

        /// <summary>Logs a message plus a full exception dump.</summary>
        void Error(string message, System.Exception exception);

        void Write(LogLevel level, string message);
    }
}
