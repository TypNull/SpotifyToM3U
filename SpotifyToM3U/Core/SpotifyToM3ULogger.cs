using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LogLevel = NLog.LogLevel;

namespace SpotifyToM3U.Core
{
    /// <summary>
    /// Central logging configuration and factory for the SpotifyToM3U application.
    /// Provides clean, structured logging with minimal configuration overhead.
    /// </summary>
    public static class SpotifyToM3ULogger
    {
        private static bool _isConfigured;

        /// <summary>
        /// Configures NLog for the application with app data folder logging.
        /// Call this once during application startup.
        /// </summary>
        /// <param name="environment">The host environment (use null for non-hosted applications)</param>
        /// <param name="appDataSubFolder">Optional subfolder within AppData (defaults to "SpotifyToM3U")</param>
        public static void ConfigureNLog(IHostEnvironment? environment = null, string appDataSubFolder = "SpotifyToM3U")
        {
            if (_isConfigured)
                throw new InvalidOperationException("NLog has already been configured.");

            _isConfigured = true;
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logFolder = Path.Combine(appDataPath, appDataSubFolder, "logs");
            Directory.CreateDirectory(logFolder);
            GlobalDiagnosticsContext.Set("basedir", Path.GetDirectoryName(logFolder));
            GlobalDiagnosticsContext.Set("logdir", logFolder);

            LoggingConfiguration? config = LogManager.Configuration;

            if (config != null)
            {
                bool isDevelopment = environment?.IsDevelopment() ?? Debugger.IsAttached;
                if (isDevelopment)
                {
                    foreach (LoggingRule rule in config.LoggingRules)
                    {
                        if (rule.Targets.Any(t => t.Name == "consoleTarget"))
                        {
                            rule.EnableLoggingForLevels(LogLevel.Trace, LogLevel.Fatal);
                        }
                    }
                }

                // Enable debugger logging when debugger is attached
                if (Debugger.IsAttached)
                {
                    foreach (LoggingRule rule in config.LoggingRules)
                    {
                        if (rule.Targets.Any(t => t.Name == "debuggerTarget"))
                        {
                            rule.EnableLoggingForLevels(LogLevel.Debug, LogLevel.Fatal);
                        }
                        if (rule.Targets.Any(t => t.Name == "vsDebugTarget"))
                        {
                            rule.EnableLoggingForLevels(LogLevel.Debug, LogLevel.Fatal);
                        }
                    }
                }

                LogManager.Configuration = config;

                // Log initial startup information
                Logger logger = LogManager.GetLogger("SpotifyToM3ULogger");
                logger.Info($"SpotifyToM3U logging initialized");
                logger.Debug($"Log directory: {logFolder}");
                logger.Debug($"Environment: {environment?.EnvironmentName ?? "Desktop"}");
                logger.Debug($"Console logging: {(isDevelopment ? "Enabled" : "Disabled")}");
                logger.Debug($"Debugger logging: {(Debugger.IsAttached ? "Enabled" : "Disabled")}");
            }
        }

        /// <summary>
        /// Gets an NLog logger for the specified type.
        /// Use this method for type-safe logger creation.
        /// </summary>
        /// <param name="type">The type requesting the logger</param>
        /// <returns>Configured NLog logger instance</returns>
        public static Logger GetLogger(Type type) => LogManager.GetLogger(type.FullName ?? "unknown");

        /// <summary>
        /// Gets an NLog logger with the specified name.
        /// Use this for custom logger names or categories.
        /// </summary>
        /// <param name="name">The logger name/category</param>
        /// <returns>Configured NLog logger instance</returns>
        public static Logger GetLogger(string name) => LogManager.GetLogger(name);

        /// <summary>
        /// Gets an NLog logger for the type of the specified object.
        /// Convenience method for object instances.
        /// </summary>
        /// <param name="obj">The object requesting the logger</param>
        /// <returns>Configured NLog logger instance</returns>
        public static Logger GetLogger(object obj) => GetLogger(obj.GetType());

        /// <summary>
        /// Creates a logger specifically for frontend/UI components.
        /// Helps organize logs by frontend component type.
        /// </summary>
        /// <param name="component">The frontend component name</param>
        /// <returns>Configured NLog logger instance</returns>
        public static Logger GetFrontendLogger(string component) => LogManager.GetLogger($"Frontend.{component}");

        /// <summary>
        /// Logs a message from frontend components with structured data.
        /// Useful for logging frontend events with additional context.
        /// </summary>
        /// <param name="level">Log level (debug, info, warn, error)</param>
        /// <param name="component">The frontend component name</param>
        /// <param name="message">The log message</param>
        /// <param name="data">Optional structured data to include</param>
        public static void LogFrontendMessage(string level, string component, string message, object? data = null)
        {
            Logger logger = GetFrontendLogger(component);

            string logMessage = data != null
                ? $"{message} | Data: {System.Text.Json.JsonSerializer.Serialize(data)}"
                : message;

            switch (level.ToLowerInvariant())
            {
                case "debug":
                    logger.Debug(logMessage);
                    break;
                case "info":
                    logger.Info(logMessage);
                    break;
                case "warn":
                    logger.Warn(logMessage);
                    break;
                case "error":
                    logger.Error(logMessage);
                    break;
                default:
                    logger.Info($"[{level}] {logMessage}");
                    break;
            }
        }
    }
}
