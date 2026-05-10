using System;
using System.IO;

namespace DeezerServer
{
    internal static class Logger
    {
        private static readonly object _fileLock = new object();

        private const string LogFileName = "server.log";

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Clear()
        {
            lock (_fileLock)
            {
                File.WriteAllText(LogFileName, string.Empty);
            }
        }

        private static void Write(string level, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

            lock (_fileLock)
            {
                Console.WriteLine(line);
                File.AppendAllText(LogFileName, line + Environment.NewLine);
            }
        }
    }
}