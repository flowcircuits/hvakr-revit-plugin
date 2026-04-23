using System;
using System.IO;

namespace LoggerApi
{
    /// <summary>
    /// Central place for file logging.
    /// </summary>
    public static class Logger
    {
        private const string LogFilePath = @"C:\Temp\RevitApiLog.txt";

        static Logger()
        {
            // Ensure directory
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
        }

        public static void LogMessage(string msg)
        {
            File.AppendAllText(LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} -  INFO: {msg}\r\n");
        }

        public static void LogError(string msg)
        {
            File.AppendAllText(LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR: {msg}\r\n");
        }
    }
}
