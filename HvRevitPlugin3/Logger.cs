using System;
using System.IO;
using Autodesk.Revit.DB;

namespace SimpleBuildingPlugin
{
    /// <summary>
    /// Central place for file logging.
    /// </summary>
    public static class Logger
    {
        private const string LogFilePath = @"C:\Temp\RevitPluginLog.txt";

        static Logger()
        {
            // Ensure directory
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
        }

        public static void LogMessage(string msg)
        {
            File.AppendAllText(LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INFO: {msg}\r\n");
        }

        public static void LogError(string msg)
        {
            File.AppendAllText(LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR: {msg}\r\n");
        }

        public static void LogElementCreation(ElementId id, string elementType)
        {
            File.AppendAllText(LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - CREATED {elementType}, ElementId={id}\r\n");
        }
    }
}
