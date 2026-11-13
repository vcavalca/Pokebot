using Pokebot.Models;
using System;
using System.IO;

namespace Pokebot.Utils
{
    public static class Log
    {
        public delegate void LogEventHandler(LogEventArgs e);

        public static event LogEventHandler? LogReceived;

        private static readonly object _fileLock = new object();
        private static bool _fileFailed;
        private static bool _fileChecked;
        private const long MaxFileBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Every log line is also appended here, so a run can be inspected (or sent) after the fact.
        /// </summary>
        public static string FilePath { get; } = Path.Combine(SettingsConfig.GetDirectory(), "pokebot.log");

        public static void Debug(string message)
        {
            Send(LogLevel.Debug, message);
        }

        public static void Info(string message)
        {
            Send(LogLevel.Info, message);
        }

        public static void Warn(string message)
        {
            Send(LogLevel.Warn, message);
        }

        public static void Error(string message)
        {
            Send(LogLevel.Error, message);
        }

        public static void Error(Exception exception)
        {
            Send(LogLevel.Error, exception.Message, exception);
        }

        public static void Fatal(string message)
        {
            Send(LogLevel.Fatal, message);
        }

        public static void Send(LogLevel level, string message, Exception? exception = null)
        {
            WriteToFile(level, message, exception);

            // Debug lines (state changes, inputs...) are written several times per encounter: file only,
            // so the Logs tab doesn't grow without limit during long hunts
            if (level != LogLevel.Debug)
            {
                LogReceived?.Invoke(new LogEventArgs(level, message, exception));
            }
        }

        private static void WriteToFile(LogLevel level, string message, Exception? exception)
        {
            if (_fileFailed)
            {
                return;
            }

            try
            {
                lock (_fileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    if (!_fileChecked)
                    {
                        // Keep one previous log, start a new one when it gets big
                        _fileChecked = true;
                        var info = new FileInfo(FilePath);
                        if (info.Exists && info.Length > MaxFileBytes)
                        {
                            var old = Path.ChangeExtension(FilePath, ".old.log");
                            File.Delete(old);
                            File.Move(FilePath, old);
                        }
                    }

                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    if (exception != null)
                    {
                        line += Environment.NewLine + exception;
                    }

                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Logging must never break the bot: stop writing the file and report it once in the UI
                _fileFailed = true;
                LogReceived?.Invoke(new LogEventArgs(LogLevel.Warn, $"Log file disabled ({FilePath}): {ex.Message}"));
            }
        }
    }

    public record LogEventArgs(LogLevel Level, string Message, Exception? Exception = null) { }
}
