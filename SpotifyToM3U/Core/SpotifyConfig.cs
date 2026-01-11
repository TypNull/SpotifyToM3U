using NLog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SpotifyToM3U.Core
{
    public class SpotifyConfig
    {
        public string ClientId { get; set; } = "your_client_id_here";
        public string ClientSecret { get; set; } = "your_client_secret_here";
        public string RedirectUri { get; set; } = "spotifytom3u://callback/oauth";
        public List<string> Scopes { get; set; } = new()
        {
            SpotifyAPI.Web.Scopes.PlaylistReadPrivate,
            SpotifyAPI.Web.Scopes.PlaylistReadCollaborative,
            SpotifyAPI.Web.Scopes.UserLibraryRead,
            SpotifyAPI.Web.Scopes.UserReadPrivate
        };
    }

    public class SpotifyAuthToken
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string TokenType { get; set; } = "Bearer";

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);
    }

    public interface ISpotifyService
    {
        bool IsAuthenticated { get; }
        string CurrentUserName { get; }
        bool IsInitialized { get; }
        bool IsAuthenticating { get; }
        event EventHandler<bool> AuthenticationStateChanged;

        Task InitializeAsync();
        Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);
        void CancelAuthentication();
        void HandleOAuthCallback(string url);
        Task LogoutAsync();
        Task<IEnumerable<FullPlaylist>> GetUserPlaylistsAsync();
        Task<FullPlaylist> GetPlaylistAsync(string playlistId);
        Task<List<PlaylistTrack<IPlayableItem>>> GetAllPlaylistTracksAsync(string playlistId);
        Task<FullTrack?> GetTrackAsync(string trackId);
    }

    public class SpotifyService : ISpotifyService
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(SpotifyService));

        private SpotifyConfig _config;
        private readonly string _tokenFilePath;
        private SpotifyClient? _spotify;
        private SpotifyClient? _publicSpotify; // For public access without user authentication
        private SpotifyAuthToken? _currentToken;
        private readonly object _initializationLock = new();
        private volatile bool _isInitialized = false;
        private Task? _initializationTask;

        // For OAuth callback handling
        private TaskCompletionSource<string>? _authorizationCodeTcs;
        private readonly object _authLock = new();

        public bool IsAuthenticated => _spotify != null && _currentToken != null && !_currentToken.IsExpired;
        public string CurrentUserName { get; private set; } = string.Empty;
        public bool IsInitialized => _isInitialized;
        public bool IsAuthenticating { get; private set; } = false;

        public event EventHandler<bool>? AuthenticationStateChanged;

        public SpotifyService()
        {
            _logger.Debug("Initializing SpotifyService");

            _config = LoadConfig();
            _tokenFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyToM3U",
                "spotify_token.json"
            );

            _logger.Debug($"Token file path: {_tokenFilePath}");
        }

        public async Task InitializeAsync()
        {
            // Use double-checked locking pattern for thread safety
            if (_isInitialized)
                return;

            lock (_initializationLock)
            {
                if (_isInitialized)
                    return;

                // If no task is running, start one
                if (_initializationTask == null)
                {
                    _initializationTask = InitializeInternalAsync();
                }
            }

            // Wait for the initialization task to complete
            await _initializationTask;
        }

        private async Task InitializeInternalAsync()
        {
            // Double check if already initialized
            if (_isInitialized)
                return;

            try
            {
                _logger.Info("Starting Spotify service initialization");

                // Initialize public client for accessing public playlists
                await InitializePublicClientAsync();

                // Try to restore user authentication
                bool authRestored = await TryLoadStoredTokenAsync();

                _logger.Info($"Spotify service initialization completed. Auth restored: {authRestored}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Spotify service initialization failed");
            }
            finally
            {
                // Always mark as initialized, even if there was an error
                _isInitialized = true;
            }
        }

        private async Task InitializePublicClientAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.ClientId) || _config.ClientId == "your_client_id_here" ||
                    string.IsNullOrEmpty(_config.ClientSecret) || _config.ClientSecret == "your_client_secret_here")
                {
                    _logger.Warn("Spotify credentials not configured, public access disabled");
                    return;
                }

                // Use Client Credentials flow for public access
                SpotifyClientConfig config = SpotifyClientConfig.CreateDefault();
                ClientCredentialsRequest request = new(_config.ClientId, _config.ClientSecret);
                ClientCredentialsTokenResponse response = await new OAuthClient(config).RequestToken(request);

                _publicSpotify = new SpotifyClient(config.WithToken(response.AccessToken));
                _logger.Info("Public Spotify client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize public Spotify client");
                _publicSpotify = null;
            }
        }

        private SpotifyConfig LoadConfig()
        {
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyToM3U",
                "spotify_config.json"
            );

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    SpotifyConfig? config = JsonSerializer.Deserialize<SpotifyConfig>(json);
                    if (config != null)
                    {
                        _logger.Debug("Loaded configuration from file");
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error loading Spotify configuration");
                }
            }

            // No saved config - return default (will trigger setup window)
            // Test credentials are handled in the setup window, not here
            _logger.Debug("No saved config found, will show setup window");
            SpotifyConfig defaultConfig = new();
            SaveConfig(defaultConfig);
            return defaultConfig;
        }

        private void SaveConfig(SpotifyConfig config)
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SpotifyToM3U",
                    "spotify_config.json"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving Spotify configuration");
            }
        }

        public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            _logger.Info("Starting Spotify authentication process");

            try
            {
                IsAuthenticating = true;
                await InitializeAsync(); // Ensure initialization is complete

                if (string.IsNullOrEmpty(_config.ClientId) || _config.ClientId == "your_client_id_here")
                {
                    // Show setup window
                    MVVM.View.Windows.SpotifySetupWindow setupWindow = new();
                    setupWindow.ShowDialog();

                    if (!setupWindow.ConfigurationSaved)
                    {
                        return false;
                    }

                    // Reload configuration
                    _config = LoadConfig();

                    // If still no valid config, check for embedded test credentials
                    if (string.IsNullOrEmpty(_config.ClientId) || _config.ClientId == "your_client_id_here")
                    {
                        if (EmbeddedSecrets.AreAvailable)
                        {
                            _logger.Info("Using embedded test credentials for authentication");
                            _config = new SpotifyConfig
                            {
                                ClientId = EmbeddedSecrets.SpotifyClientId,
                                ClientSecret = EmbeddedSecrets.SpotifyClientSecret,
                                RedirectUri = "spotifytom3u://callback/oauth",
                                Scopes = new()
                                {
                                    SpotifyAPI.Web.Scopes.PlaylistReadPrivate,
                                    SpotifyAPI.Web.Scopes.PlaylistReadCollaborative,
                                    SpotifyAPI.Web.Scopes.UserLibraryRead,
                                    SpotifyAPI.Web.Scopes.UserReadPrivate
                                }
                            };
                        }
                        else
                        {
                            throw new InvalidOperationException("Spotify API configuration is still incomplete.");
                        }
                    }

                    // Reinitialize public client with new credentials
                    await InitializePublicClientAsync();
                }

                lock (_authLock)
                {
                    _authorizationCodeTcs = new TaskCompletionSource<string>();
                }

                LoginRequest request = new(new Uri(_config.RedirectUri), _config.ClientId, LoginRequest.ResponseType.Code)
                {
                    Scope = _config.Scopes
                };

                Uri authUrl = request.ToUri();
                _logger.Debug($"Opening browser with authorization URL: {authUrl}");

                BrowserUtil.Open(authUrl);

                string authorizationCode;
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromMinutes(5));

                    Task<string> completionTask = _authorizationCodeTcs.Task;
                    Task timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                    Task completedTask = await Task.WhenAny(completionTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.Info("OAuth authorization cancelled by user");
                            throw new OperationCanceledException("Authentication cancelled.", cancellationToken);
                        }
                        _logger.Warn("OAuth authorization timed out");
                        throw new TimeoutException("Authorization timed out. Please try again.");
                    }

                    authorizationCode = await completionTask;
                }

                _logger.Debug("Authorization code received, exchanging for token");

                await ExchangeCodeForTokenAsync(authorizationCode);

                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Authentication was cancelled");
                lock (_authLock)
                {
                    _authorizationCodeTcs?.TrySetCanceled();
                    _authorizationCodeTcs = null;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Authentication process failed");
                lock (_authLock)
                {
                    _authorizationCodeTcs?.TrySetException(ex);
                    _authorizationCodeTcs = null;
                }
                throw;
            }
            finally
            {
                IsAuthenticating = false;
            }
        }

        public void CancelAuthentication()
        {
            _logger.Info("CancelAuthentication called");
            lock (_authLock)
            {
                _authorizationCodeTcs?.TrySetCanceled();
                _authorizationCodeTcs = null;
            }
        }

        /// <summary>
        /// Handles OAuth callback URLs received via the custom URL scheme.
        /// </summary>
        public void HandleOAuthCallback(string url)
        {
            try
            {
                _logger.Debug($"Processing OAuth callback URL: {url}");

                Uri uri = new Uri(url);
                string query = uri.Query;

                NameValueCollection queryParams = HttpUtility.ParseQueryString(query);
                string? code = queryParams["code"];
                string? error = queryParams["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    _logger.Error($"OAuth error received: {error}");
                    lock (_authLock)
                    {
                        _authorizationCodeTcs?.TrySetException(new Exception($"Authorization error: {error}"));
                        _authorizationCodeTcs = null;
                    }
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    _logger.Error("OAuth callback received without authorization code");
                    lock (_authLock)
                    {
                        _authorizationCodeTcs?.TrySetException(new Exception("No authorization code received"));
                        _authorizationCodeTcs = null;
                    }
                    return;
                }

                _logger.Info("Authorization code extracted from callback");

                lock (_authLock)
                {
                    _authorizationCodeTcs?.TrySetResult(code);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling OAuth callback");
                lock (_authLock)
                {
                    _authorizationCodeTcs?.TrySetException(ex);
                    _authorizationCodeTcs = null;
                }
            }
        }

        private async Task ExchangeCodeForTokenAsync(string authorizationCode)
        {
            try
            {
                _logger.Debug("Exchanging authorization code for access token");

                AuthorizationCodeTokenRequest tokenRequest = new(
                    _config.ClientId,
                    _config.ClientSecret,
                    authorizationCode,
                    new Uri(_config.RedirectUri)
                );

                AuthorizationCodeTokenResponse tokenResponse = await new OAuthClient().RequestToken(tokenRequest);

                _currentToken = new SpotifyAuthToken
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                    TokenType = tokenResponse.TokenType
                };

                SpotifyClientConfig config = SpotifyClientConfig.CreateDefault()
                    .WithAuthenticator(new AuthorizationCodeAuthenticator(_config.ClientId, _config.ClientSecret, tokenResponse));

                _spotify = new SpotifyClient(config);

                // Get user info
                try
                {
                    PrivateUser user = await _spotify.UserProfile.Current();
                    CurrentUserName = user.DisplayName ?? user.Id;
                    _logger.Debug($"Retrieved user profile: {CurrentUserName}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not retrieve user profile, using default name");
                    CurrentUserName = "Spotify User";
                }

                // Save token
                await SaveTokenAsync(_currentToken);

                _logger.Info($"Authentication successful for user: {CurrentUserName}");
                AuthenticationStateChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Token exchange failed");
                await LogoutAsync();
                throw;
            }
        }

        private async Task<bool> TryLoadStoredTokenAsync()
        {
            try
            {
                if (!File.Exists(_tokenFilePath))
                {
                    _logger.Debug("No stored token found");
                    return false;
                }

                _logger.Debug("Found stored token file, attempting to load");
                string json = await File.ReadAllTextAsync(_tokenFilePath);

                _currentToken = JsonSerializer.Deserialize<SpotifyAuthToken>(json);

                if (_currentToken == null)
                {
                    _logger.Warn("Failed to deserialize stored token");
                    return false;
                }

                _logger.Debug($"Token IsExpired: {_currentToken.IsExpired}");

                if (_currentToken.IsExpired)
                {
                    _logger.Debug("Stored token is expired, attempting refresh");
                    return await TryRefreshTokenAsync();
                }

                _logger.Debug("Token appears valid, testing with Spotify API");

                // Create token response from stored token
                AuthorizationCodeTokenResponse tokenResponse = new()
                {
                    AccessToken = _currentToken.AccessToken,
                    RefreshToken = _currentToken.RefreshToken,
                    ExpiresIn = (int)Math.Max(1, (_currentToken.ExpiresAt - DateTime.UtcNow).TotalSeconds),
                    TokenType = _currentToken.TokenType
                };

                SpotifyClientConfig config = SpotifyClientConfig.CreateDefault()
                    .WithAuthenticator(new AuthorizationCodeAuthenticator(_config.ClientId, _config.ClientSecret, tokenResponse));

                _spotify = new SpotifyClient(config);

                // Test the connection and get user info
                try
                {
                    PrivateUser user = await _spotify.UserProfile.Current();
                    CurrentUserName = user.DisplayName ?? user.Id;
                    _logger.Info($"Successfully restored authentication for user: {CurrentUserName}");
                    AuthenticationStateChanged?.Invoke(this, true);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Token validation failed, attempting refresh");
                    // Try refreshing the token
                    return await TryRefreshTokenAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load stored token");
                await LogoutAsync();
                return false;
            }
        }

        private async Task<bool> TryRefreshTokenAsync()
        {
            try
            {
                if (_currentToken?.RefreshToken == null)
                {
                    _logger.Debug("No refresh token available");
                    return false;
                }

                _logger.Debug("Attempting to refresh token");

                AuthorizationCodeRefreshRequest refreshRequest = new(_config.ClientId, _config.ClientSecret, _currentToken.RefreshToken);
                AuthorizationCodeRefreshResponse refreshResponse = await new OAuthClient().RequestToken(refreshRequest);

                _currentToken.AccessToken = refreshResponse.AccessToken;
                _currentToken.ExpiresAt = DateTime.UtcNow.AddSeconds(refreshResponse.ExpiresIn);
                if (!string.IsNullOrEmpty(refreshResponse.RefreshToken))
                {
                    _currentToken.RefreshToken = refreshResponse.RefreshToken;
                }

                // Create a token response object for the authenticator
                AuthorizationCodeTokenResponse tokenResponse = new()
                {
                    AccessToken = refreshResponse.AccessToken,
                    RefreshToken = _currentToken.RefreshToken,
                    ExpiresIn = refreshResponse.ExpiresIn,
                    TokenType = refreshResponse.TokenType
                };

                SpotifyClientConfig config = SpotifyClientConfig.CreateDefault()
                    .WithAuthenticator(new AuthorizationCodeAuthenticator(_config.ClientId, _config.ClientSecret, tokenResponse));

                _spotify = new SpotifyClient(config);

                // Test the refreshed token
                PrivateUser user = await _spotify.UserProfile.Current();
                CurrentUserName = user.DisplayName ?? user.Id;

                // Save the refreshed token
                await SaveTokenAsync(_currentToken);

                _logger.Info($"Token refreshed successfully for user: {CurrentUserName}");
                AuthenticationStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Token refresh failed");
                await LogoutAsync();
                return false;
            }
        }

        private async Task SaveTokenAsync(SpotifyAuthToken token)
        {
            try
            {
                string directory = Path.GetDirectoryName(_tokenFilePath)!;
                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_tokenFilePath, json);
                _logger.Debug("Token saved successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save token");
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                _logger.Info("Logging out user");

                _spotify = null;
                _currentToken = null;
                CurrentUserName = string.Empty;

                lock (_authLock)
                {
                    _authorizationCodeTcs?.TrySetCanceled();
                    _authorizationCodeTcs = null;
                }

                if (File.Exists(_tokenFilePath))
                {
                    File.Delete(_tokenFilePath);
                    _logger.Debug("Stored token deleted");
                }

                AuthenticationStateChanged?.Invoke(this, false);
                _logger.Info("Logout completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during logout process");
            }
        }

        public async Task<IEnumerable<FullPlaylist>> GetUserPlaylistsAsync()
        {
            if (!IsAuthenticated)
                throw new InvalidOperationException("Not authenticated - user playlists require authentication");

            try
            {
                // Use PaginateAll like in the official examples
                IList<FullPlaylist> playlists = await _spotify!.PaginateAll(await _spotify.Playlists.CurrentUsers());
                return playlists;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch user playlists");
                throw;
            }
        }

        public async Task<FullPlaylist> GetPlaylistAsync(string playlistId)
        {
            await InitializeAsync(); // Ensure initialization is complete

            try
            {
                // Try with authenticated client first if available
                if (IsAuthenticated)
                {
                    try
                    {
                        return await _spotify!.Playlists.Get(playlistId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to fetch playlist with authenticated client, trying public client");
                        // Fall through to public client
                    }
                }

                // Try with public client for public playlists
                if (_publicSpotify != null)
                {
                    try
                    {
                        return await _publicSpotify.Playlists.Get(playlistId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to fetch playlist {playlistId} with public client");
                        throw new InvalidOperationException($"Cannot access playlist {playlistId}. It may be private or the playlist ID is invalid.", ex);
                    }
                }

                throw new InvalidOperationException("No Spotify client available. Please check your API configuration.");
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.Error(ex, $"Unexpected error fetching playlist {playlistId}");
                throw;
            }
        }

        public async Task<List<PlaylistTrack<IPlayableItem>>> GetAllPlaylistTracksAsync(string playlistId)
        {
            await InitializeAsync(); // Ensure initialization is complete

            try
            {
                // Try with authenticated client first if available
                if (IsAuthenticated)
                {
                    try
                    {
                        IList<PlaylistTrack<IPlayableItem>> tracks = await _spotify!.PaginateAll(await _spotify.Playlists.GetItems(playlistId));
                        return tracks.ToList();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to fetch playlist tracks with authenticated client, trying public client");
                        // Fall through to public client
                    }
                }

                // Try with public client for public playlists
                if (_publicSpotify != null)
                {
                    try
                    {
                        IList<PlaylistTrack<IPlayableItem>> tracks = await _publicSpotify.PaginateAll(await _publicSpotify.Playlists.GetItems(playlistId));
                        return tracks.ToList();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to fetch tracks for playlist {playlistId} with public client");
                        throw new InvalidOperationException($"Cannot access tracks for playlist {playlistId}. It may be private or the playlist ID is invalid.", ex);
                    }
                }

                throw new InvalidOperationException("No Spotify client available. Please check your API configuration.");
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.Error(ex, $"Unexpected error fetching tracks for playlist {playlistId}");
                throw;
            }
        }

        public async Task<FullTrack?> GetTrackAsync(string trackId)
        {
            await InitializeAsync(); // Ensure initialization is complete

            try
            {
                // Try with authenticated client first if available
                if (IsAuthenticated)
                {
                    try
                    {
                        return await _spotify!.Tracks.Get(trackId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to fetch track with authenticated client, trying public client");
                        // Fall through to public client
                    }
                }

                // Try with public client
                if (_publicSpotify != null)
                {
                    try
                    {
                        return await _publicSpotify.Tracks.Get(trackId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, $"Failed to fetch track {trackId} with public client");
                        return null;
                    }
                }

                _logger.Warn("No Spotify client available for track fetching");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected error fetching track {trackId}");
                return null;
            }
        }
    }
}