using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using SpotifyToM3U.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace SpotifyToM3U.MVVM.ViewModel
{
    public partial class SpotifySetupVM : ObservableObject
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(SpotifySetupVM));
        private readonly string _configPath;

        [ObservableProperty]
        private string _clientId = string.Empty;

        [ObservableProperty]
        private string _clientSecret = string.Empty;

        [ObservableProperty]
        private bool _isSaving = false;

        [ObservableProperty]
        private string _statusMessage = "Enter your Spotify API credentials to get started.";

        [ObservableProperty]
        private bool _hasValidationErrors = false;

        public bool ConfigurationSaved { get; private set; } = false;

        public event EventHandler? RequestClose;

        public SpotifySetupVM()
        {
            _logger.Debug("Initializing SpotifySetupVM");

            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyToM3U",
                "spotify_config.json"
            );

            LoadExistingConfiguration();
            _logger.Info($"SpotifySetupVM initialized. Config path: {_configPath}");
        }

        [RelayCommand]
        private void OpenDashboard()
        {
            _logger.Debug("Opening Spotify Developer Dashboard");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://developer.spotify.com/dashboard",
                    UseShellExecute = true
                });
                StatusMessage = "Browser opened. Create your app and come back with the credentials.";
                _logger.Info("Successfully opened Spotify Developer Dashboard in browser");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open browser: {ex.Message}";
                _logger.Error(ex, "Failed to open Spotify Developer Dashboard");
            }
        }

        [RelayCommand]
        private void CopyRedirectUri()
        {
            _logger.Debug("Copying redirect URI to clipboard");

            try
            {
                Clipboard.SetText("http://127.0.0.1:5000/callback");
                StatusMessage = "Redirect URI copied to clipboard! Paste this when creating your Spotify app.";
                _logger.Info("Redirect URI copied to clipboard successfully");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to copy to clipboard: {ex.Message}";
                _logger.Error(ex, "Failed to copy redirect URI to clipboard");
            }
        }

        [RelayCommand]
        private async Task SaveConfigurationAsync()
        {
            _logger.Info("Starting Spotify configuration save process");

            try
            {
                IsSaving = true;
                HasValidationErrors = false;
                StatusMessage = "Validating credentials...";

                // Validate inputs
                if (!ValidateInputs())
                {
                    HasValidationErrors = true;
                    _logger.Warn("Configuration validation failed");
                    return;
                }

                _logger.Debug("Creating Spotify configuration object");

                // Create configuration
                SpotifyConfig config = new()
                {
                    ClientId = ClientId.Trim(),
                    ClientSecret = ClientSecret.Trim(),
                    RedirectUri = "http://127.0.0.1:5000/callback",
                    Scopes = new()
                    {
                        SpotifyAPI.Web.Scopes.PlaylistReadPrivate,
                        SpotifyAPI.Web.Scopes.PlaylistReadCollaborative,
                        SpotifyAPI.Web.Scopes.UserLibraryRead,
                        SpotifyAPI.Web.Scopes.UserReadPrivate
                    }
                };

                // Save configuration
                await SaveConfigAsync(config);

                ConfigurationSaved = true;
                StatusMessage = "Configuration saved successfully!";
                _logger.Info($"Spotify configuration saved successfully to: {_configPath}");

                // Close window after a short delay
                await Task.Delay(1000);
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to save configuration: {ex.Message}";
                HasValidationErrors = true;
                _logger.Error(ex, "Failed to save Spotify configuration");
            }
            finally
            {
                IsSaving = false;
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            _logger.Debug("Spotify setup cancelled by user");
            ConfigurationSaved = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private bool ValidateInputs()
        {
            string clientId = ClientId.Trim();
            string clientSecret = ClientSecret.Trim();

            if (string.IsNullOrEmpty(clientId))
            {
                StatusMessage = "Please enter your Client ID.";
                return false;
            }

            if (string.IsNullOrEmpty(clientSecret))
            {
                StatusMessage = "Please enter your Client Secret.";
                return false;
            }

            // Validate Client ID format (basic validation)
            if (clientId.Length < 20 || !Regex.IsMatch(clientId, @"^[a-zA-Z0-9]+$"))
            {
                StatusMessage = "Client ID format appears to be invalid. Please check and try again.";
                return false;
            }

            // Validate Client Secret format (basic validation)
            if (clientSecret.Length < 20 || !Regex.IsMatch(clientSecret, @"^[a-zA-Z0-9]+$"))
            {
                StatusMessage = "Client Secret format appears to be invalid. Please check and try again.";
                return false;
            }

            return true;
        }

        private void LoadExistingConfiguration()
        {
            _logger.Debug("Attempting to load existing Spotify configuration");

            try
            {
                if (File.Exists(_configPath))
                {
                    _logger.Debug($"Found existing config file: {_configPath}");

                    string json = File.ReadAllText(_configPath);
                    SpotifyConfig? config = JsonSerializer.Deserialize<SpotifyConfig>(json);

                    if (config != null)
                    {
                        if (!string.IsNullOrEmpty(config.ClientId) && config.ClientId != "your_client_id_here")
                        {
                            ClientId = config.ClientId;
                            _logger.Debug("Loaded existing Client ID");
                        }

                        if (!string.IsNullOrEmpty(config.ClientSecret) && config.ClientSecret != "your_client_secret_here")
                        {
                            ClientSecret = config.ClientSecret;
                            _logger.Debug("Loaded existing Client Secret");
                        }

                        if (!string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret))
                        {
                            StatusMessage = "Existing configuration loaded. You can update the credentials if needed.";
                            _logger.Info("Existing Spotify configuration loaded successfully");
                        }
                    }
                }
                else
                {
                    _logger.Debug("No existing configuration file found");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading existing configuration: {ex.Message}";
                _logger.Error(ex, "Error loading existing Spotify configuration");
            }
        }

        private async Task SaveConfigAsync(SpotifyConfig config)
        {
            _logger.Debug($"Saving Spotify configuration to: {_configPath}");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configPath, json);
                _logger.Debug("Spotify configuration file written successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save configuration to: {_configPath}");
                throw;
            }
        }
    }
}