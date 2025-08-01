using NLog;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpotifyToM3U.Core
{
    public class UserSettings
    {
        public bool DetailedLogging { get; set; } = false;
        public string ExportPath { get; set; } = string.Empty;
        public bool ExportAsRelative { get; set; } = false;
    }

    public interface IUserSettingsService
    {
        UserSettings Settings { get; }
        bool DetailedLogging { get; set; }
        event EventHandler<bool> DetailedLoggingChanged;
        void Save();
        void Load();
    }

    public class UserSettingsService : IUserSettingsService
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(UserSettingsService));
        private readonly string _settingsPath;
        private UserSettings _settings = new();

        public UserSettings Settings => _settings;

        public bool DetailedLogging
        {
            get => _settings.DetailedLogging;
            set
            {
                if (_settings.DetailedLogging != value)
                {
                    _settings.DetailedLogging = value;
                    _logger.Info($"Detailed logging {(value ? "enabled" : "disabled")}");
                    DetailedLoggingChanged?.Invoke(this, value);
                    Save();
                    UpdateLoggingConfiguration(value);
                }
            }
        }

        public event EventHandler<bool>? DetailedLoggingChanged;

        public UserSettingsService()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyToM3U",
                "user_settings.json"
            );
            Load();
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(_settingsPath)!;
                Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                _logger.Debug($"User settings saved to: {_settingsPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save user settings");
            }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                    _logger.Debug($"User settings loaded from: {_settingsPath}");
                }
                else
                {
                    _logger.Debug("No existing user settings found, using defaults");
                    _settings = new UserSettings();
                }

                // Apply the current detailed logging setting
                UpdateLoggingConfiguration(_settings.DetailedLogging);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load user settings, using defaults");
                _settings = new UserSettings();
            }
        }

        private static void UpdateLoggingConfiguration(bool detailedLogging)
        {
            try
            {
                NLog.Config.LoggingConfiguration? config = LogManager.Configuration;
                if (config == null) return;

                foreach (NLog.Config.LoggingRule rule in config.LoggingRules)
                {
                    if (rule.Targets.Any(t => t.Name == "consoleTarget"))
                    {
                        rule.SetLoggingLevels(!detailedLogging ? LogLevel.Info : LogLevel.Trace, LogLevel.Fatal
                        );
                    }

                    if (rule.Targets.Any(t => t.Name == "debuggerTarget"))
                    {
                        rule.SetLoggingLevels(
                            detailedLogging ? LogLevel.Trace : LogLevel.Info,
                           LogLevel.Fatal
                        );
                    }

                    // Enable/disable debug and trace file logging
                    if (rule.Targets.Any(t => t.Name == "appDebugFile"))
                    {
                        if (detailedLogging)
                        {
                            rule.SetLoggingLevels(LogLevel.Debug, LogLevel.Fatal);
                        }
                        else
                        {
                            rule.DisableLoggingForLevels(LogLevel.Trace, LogLevel.Debug);
                        }
                    }

                    if (rule.Targets.Any(t => t.Name == "appTraceFile"))
                    {
                        if (detailedLogging)
                        {
                            rule.SetLoggingLevels(LogLevel.Trace, LogLevel.Fatal);
                        }
                        else
                        {
                            rule.DisableLoggingForLevels(LogLevel.Trace, LogLevel.Debug);
                        }
                    }
                }

                LogManager.ReconfigExistingLoggers();
                _logger.Info($"Logging configuration updated - Detailed logging: {(detailedLogging ? "ON" : "OFF")}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update logging configuration");
            }
        }
    }
}
