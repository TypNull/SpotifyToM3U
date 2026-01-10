using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using SpotifyAPI.Web;
using SpotifyToM3U.Core;
using SpotifyToM3U.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace SpotifyToM3U.MVVM.ViewModel
{
    public partial class PlaylistInfo : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private string _imageUrl = string.Empty;

        [ObservableProperty]
        private int _trackCount = 0;

        [ObservableProperty]
        private string _owner = string.Empty;

        [ObservableProperty]
        private bool _isSelected = false;

        [ObservableProperty]
        private bool _isPublic = true;

        public PlaylistInfo() { }

        public PlaylistInfo(FullPlaylist playlist)
        {
            Id = playlist.Id!;
            Name = playlist.Name!;
            Description = playlist.Description ?? string.Empty;
            ImageUrl = playlist.Images?.FirstOrDefault()?.Url ?? string.Empty;
            TrackCount = playlist.Tracks?.Total ?? 0;
            Owner = playlist.Owner?.DisplayName ?? playlist.Owner?.Id ?? "Unknown";
            IsPublic = playlist.Public ?? false;
        }
    }

    public enum TrackFilter
    {
        All,
        FoundOnly,
        MissingOnly,
        WeakMatches,
        PerfectMatches
    }

    internal partial class SpotifyVM : ViewModelObject
    {
        #region Fields

        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(SpotifyVM));

        private readonly ISpotifyService _spotifyService;
        private readonly LibraryVM _libraryVM;

        // Store all tracks for filtering
        private List<Track> _allTracks = new();
        private readonly List<TrackMatchResult> _allMatchResults = new();

        // Cancellation for authentication
        private CancellationTokenSource? _authCancellationTokenSource;

        #endregion

        #region Observable Properties

        [ObservableProperty]
        private string _playlistIDText = string.Empty;

        [ObservableProperty]
        private bool _isAuthenticated = false;

        [ObservableProperty]
        private string _currentUser = "Not logged in";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isNext = false;

        [ObservableProperty]
        private string _statusMessage = "Connect to Spotify to get started";

        [ObservableProperty]
        private bool _isAuthenticating = false;

        [ObservableProperty]
        private ObservableCollection<PlaylistInfo> _userPlaylists = new();

        [ObservableProperty]
        private PlaylistInfo? _selectedPlaylist;

        [ObservableProperty]
        private ObservableCollection<Track> _playlistTracks = new();

        [ObservableProperty]
        private ObservableCollection<Track> _filteredTracks = new();

        [ObservableProperty]
        private bool _showPlaylists = false;

        [ObservableProperty]
        private bool _showManualInput = true;

        [ObservableProperty]
        private int _playlistFound = 0;

        [ObservableProperty]
        private int _playlistLength = 0;

        [ObservableProperty]
        private string _playlistName = "Unknown Playlist";

        // Match statistics
        [ObservableProperty]
        private int _perfectMatches = 0;

        [ObservableProperty]
        private int _goodMatches = 0;

        [ObservableProperty]
        private int _weakMatches = 0;

        [ObservableProperty]
        private int _missingTracks = 0;

        // Export statistics
        [ObservableProperty]
        private int _selectedForExport = 0;

        [ObservableProperty]
        private int _excludedFromExport = 0;

        [ObservableProperty]
        private TrackFilter _currentFilter = TrackFilter.All;

        #endregion

        #region Constructor

        public SpotifyVM(INavigationService navigation) : base(navigation)
        {
            _logger.Debug("Initializing SpotifyVM");

            try
            {
                _spotifyService = App.Current.ServiceProvider.GetRequiredService<ISpotifyService>();
                _libraryVM = App.Current.ServiceProvider.GetRequiredService<LibraryVM>();

                _spotifyService.AuthenticationStateChanged += OnAuthenticationStateChanged;
                _libraryVM.AudioFilesModifified += LibraryVM_AudioFilesModified;
                _libraryVM.PropertyChanged += LibraryVM_PropertyChanged;

                BindingOperations.EnableCollectionSynchronization(PlaylistTracks, new object());
                BindingOperations.EnableCollectionSynchronization(FilteredTracks, new object());
                BindingOperations.EnableCollectionSynchronization(UserPlaylists, new object());

                // Initialize authentication state
                IsAuthenticated = _spotifyService.IsAuthenticated;
                CurrentUser = _spotifyService.CurrentUserName;

                if (IsAuthenticated)
                {
                    StatusMessage = $"Connected as {CurrentUser}";
                    _ = LoadUserPlaylistsAsync();
                    _logger.Info($"SpotifyVM initialized - already authenticated as {CurrentUser}");
                }
                else
                {
                    _logger.Debug("SpotifyVM initialized - not authenticated");
                }

                UpdatePlaylistStats();
                _logger.Info("SpotifyVM initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize SpotifyVM");
                throw;
            }
        }

        #endregion

        #region Authentication Commands

        [RelayCommand]
        private async Task AuthenticateAsync()
        {
            _logger.Debug("AuthenticateAsync command initiated");

            try
            {
                IsLoading = true;

                if (IsAuthenticated)
                {
                    StatusMessage = "Logging out...";
                    _logger.Info("Logging out from Spotify");
                    await _spotifyService.LogoutAsync();
                }
                else
                {
                    IsAuthenticating = true;
                    StatusMessage = "Connecting to Spotify... (Press Escape or Q to cancel)";
                    _logger.Info("Starting Spotify authentication");

                    _authCancellationTokenSource?.Cancel();
                    _authCancellationTokenSource = new CancellationTokenSource();

                    bool success = await _spotifyService.AuthenticateAsync(_authCancellationTokenSource.Token);
                    if (!success)
                    {
                        if (_authCancellationTokenSource.Token.IsCancellationRequested)
                        {
                            _logger.Info("Spotify authentication cancelled by user");
                            StatusMessage = "Authentication cancelled.";
                        }
                        else
                        {
                            _logger.Warn("Spotify authentication failed");
                            StatusMessage = "Failed to connect to Spotify. Please check your configuration.";
                        }
                    }
                    else
                    {
                        _logger.Info($"Spotify authentication successful - user: {_spotifyService.CurrentUserName}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Authentication cancelled");
                StatusMessage = "Authentication cancelled.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Authentication error occurred");
                StatusMessage = $"Authentication error: {ex.Message}";
                MessageBox.Show($"Authentication failed: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                IsAuthenticating = false;
                _authCancellationTokenSource?.Dispose();
                _authCancellationTokenSource = null;
            }
        }

        [RelayCommand]
        private void CancelAuthentication()
        {
            if (IsAuthenticating)
            {
                _logger.Info("User requested authentication cancellation");
                _authCancellationTokenSource?.Cancel();
                _spotifyService.CancelAuthentication();
            }
        }

        [RelayCommand]
        private async Task LoadUserPlaylistsAsync()
        {
            if (!IsAuthenticated)
            {
                _logger.Debug("LoadUserPlaylistsAsync called but user not authenticated");
                return;
            }

            _logger.Debug("Starting to load user playlists");

            try
            {
                IsLoading = true;
                StatusMessage = "Loading your playlists...";

                UserPlaylists.Clear();
                IEnumerable<FullPlaylist> playlists = await _spotifyService.GetUserPlaylistsAsync();

                foreach (FullPlaylist playlist in playlists)
                {
                    UserPlaylists.Add(new PlaylistInfo(playlist));
                }

                ShowPlaylists = UserPlaylists.Count > 0;
                StatusMessage = $"Loaded {UserPlaylists.Count} playlists";

                _logger.Info($"Successfully loaded {UserPlaylists.Count} user playlists");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading playlists: {ex.Message}";
                _logger.Error(ex, "Error loading user playlists");
                Debug.WriteLine($"Error loading playlists: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Playlist Loading Commands

        [RelayCommand]
        private async Task LoadPlaylistAsync(PlaylistInfo? playlist = null)
        {
            try
            {
                playlist ??= SelectedPlaylist;
                if (playlist == null)
                    return;

                IsLoading = true;
                IsNext = false;
                StatusMessage = $"Loading playlist: {playlist.Name}";

                ClearCurrentPlaylist(); // This should clear everything
                SelectedPlaylist = playlist;

                List<Track> tracks = await LoadAllPlaylistTracksAsync(playlist.Id);
                _allTracks = tracks;

                // Make sure PlaylistTracks is completely clear before adding
                PlaylistTracks.Clear();

                foreach (Track track in tracks)
                {
                    PlaylistTracks.Add(track);
                }

                UpdatePlaylistStats();
                await FindTracksWithConfidenceAsync(tracks);
                UpdatePlaylistStats();
                ApplyCurrentFilter();

                if (PlaylistTracks.Any(x => x.IsLocal))
                {
                    IsNext = true;
                    StatusMessage = $"Loaded {PlaylistLength} tracks, found {PlaylistFound} locally ({PerfectMatches} perfect matches)";
                }
                else
                {
                    StatusMessage = $"Loaded {PlaylistLength} tracks, no local matches found";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading playlist: {ex.Message}";
                MessageBox.Show($"Error loading playlist: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LoadPlaylistByIdAsync()
        {
            if (string.IsNullOrWhiteSpace(PlaylistIDText))
                return;

            try
            {
                IsLoading = true;
                StatusMessage = "Loading playlist...";

                string playlistId = ExtractPlaylistId(PlaylistIDText);
                if (string.IsNullOrEmpty(playlistId))
                {
                    StatusMessage = "Invalid playlist URL or ID";
                    return;
                }

                FullPlaylist playlist = await _spotifyService.GetPlaylistAsync(playlistId);
                PlaylistInfo playlistInfo = new(playlist);

                await LoadPlaylistAsync(playlistInfo);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading playlist: {ex.Message}";
                MessageBox.Show($"Error loading playlist: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Filter Commands

        [RelayCommand]
        private void ShowAllTracks()
        {
            CurrentFilter = TrackFilter.All;
            ApplyCurrentFilter();
            StatusMessage = $"Showing all {_allTracks.Count} tracks";
        }

        [RelayCommand]
        private void ShowFoundOnly()
        {
            CurrentFilter = TrackFilter.FoundOnly;
            ApplyCurrentFilter();
            int foundCount = _allTracks.Count(t => t.IsLocal);
            StatusMessage = $"Showing {foundCount} found tracks";
        }

        [RelayCommand]
        private void ShowMissingOnly()
        {
            CurrentFilter = TrackFilter.MissingOnly;
            ApplyCurrentFilter();
            int missingCount = _allTracks.Count(t => !t.IsLocal);
            StatusMessage = $"Showing {missingCount} missing tracks";
        }

        [RelayCommand]
        private void ShowWeakMatches()
        {
            CurrentFilter = TrackFilter.WeakMatches;
            ApplyCurrentFilter();
            StatusMessage = $"Showing {FilteredTracks.Count} tracks that need review - click tracks to exclude from export";
        }

        [RelayCommand]
        private void ShowPerfectMatches()
        {
            CurrentFilter = TrackFilter.PerfectMatches;
            ApplyCurrentFilter();
            StatusMessage = $"Showing {PerfectMatches} perfect matches";
        }

        #endregion

        #region Selection Commands

        [RelayCommand]
        private void ToggleTrackSelection(Track track)
        {
            if (track != null && track.IsLocal)
            {
                track.IsSelected = !track.IsSelected;
                string status = track.IsSelected ? "included in" : "excluded from";
                StatusMessage = $"Track {status} export: {track.Title}";
                UpdateExportStats();
            }
        }

        [RelayCommand]
        private void DeselectAllVisibleTracks()
        {
            foreach (Track track in _allTracks)
            {
                track.IsSelected = false;
            }
            UpdateExportStats();
            StatusMessage = "Deselected all tracks from export";
        }

        [RelayCommand]
        private void SelectAllVisibleTracks()
        {
            foreach (Track track in _allTracks)
            {
                if (track.IsLocal)
                    track.IsSelected = true;
            }
            UpdateExportStats();
            StatusMessage = $"Selected all found tracks for export";
        }

        [RelayCommand]
        private void SelectAllWeakMatches()
        {
            foreach (Track? track in _allTracks.Where(t => t.IsLocal && t.MatchConfidence < 0.75))
            {
                track.IsSelected = true;
            }
            UpdateExportStats();
            StatusMessage = "Selected all weak matches for export";
        }

        [RelayCommand]
        private void DeselectAllWeakMatches()
        {
            foreach (Track? track in _allTracks.Where(t => t.IsLocal && t.MatchConfidence < 0.75))
            {
                track.IsSelected = false;
            }
            UpdateExportStats();
            StatusMessage = "Excluded all weak matches from export";
        }

        #endregion

        #region Navigation Commands

        [RelayCommand]
        private void Next() => Navigation.NavigateTo<ExportVM>();

        #endregion

        #region Private Methods

        private void UpdatePlaylistStats()
        {
            PlaylistLength = _allTracks.Count;
            PlaylistFound = _allTracks.Count(x => x.IsLocal);
            PlaylistName = SelectedPlaylist?.Name ?? "Unknown Playlist";

            // Calculate match quality statistics
            PerfectMatches = _allTracks.Count(t => t.IsLocal && t.MatchConfidence >= 0.95);
            GoodMatches = _allTracks.Count(t => t.IsLocal && t.MatchConfidence >= 0.75 && t.MatchConfidence < 0.95);
            WeakMatches = _allTracks.Count(t => t.IsLocal && t.MatchConfidence < 0.75);
            MissingTracks = _allTracks.Count(t => !t.IsLocal);

            UpdateExportStats();
        }

        private void UpdateExportStats()
        {
            // Calculate export statistics
            SelectedForExport = _allTracks.Count(t => t.IsIncludedInExport);
            ExcludedFromExport = _allTracks.Count(t => t.IsLocal && !t.IsSelected);
        }

        private void ApplyCurrentFilter()
        {
            IEnumerable<Track> filteredList = CurrentFilter switch
            {
                TrackFilter.FoundOnly => _allTracks.Where(t => t.IsLocal),
                TrackFilter.MissingOnly => _allTracks.Where(t => !t.IsLocal),
                TrackFilter.WeakMatches => _allTracks.Where(t => t.IsLocal && t.MatchConfidence < 0.75),
                TrackFilter.PerfectMatches => _allTracks.Where(t => t.IsLocal && t.MatchConfidence >= 0.95),
                _ => _allTracks
            };

            FilteredTracks.Clear();
            foreach (Track? track in filteredList)
            {
                FilteredTracks.Add(track);
            }
        }

        private void ClearCurrentPlaylist()
        {
            PlaylistTracks.Clear();
            FilteredTracks.Clear();
            _allTracks.Clear();
            _allMatchResults.Clear();
            UpdatePlaylistStats();
        }

        private async Task<List<Track>> LoadAllPlaylistTracksAsync(string playlistId)
        {
            List<Track> tracks = new();

            List<PlaylistTrack<IPlayableItem>> playlistTracks = await _spotifyService.GetAllPlaylistTracksAsync(playlistId);

            foreach (PlaylistTrack<IPlayableItem> item in playlistTracks)
            {
                if (item.Track is FullTrack fullTrack)
                {
                    Track track = ConvertToTrack(fullTrack);
                    tracks.Add(track);
                }
            }

            return tracks;
        }

        private Track ConvertToTrack(FullTrack spotifyTrack)
        {
            Track track = new()
            {
                Id = spotifyTrack.Id,
                Title = spotifyTrack.Name,
                CutTitle = IOManager.CutString(spotifyTrack.Name),
                Uri = spotifyTrack.Uri,
                IsLocal = false,
                Path = string.Empty,
                MatchConfidence = 0.0,
                MatchType = string.Empty,
                IsSelected = true // Default to selected
            };

            // Set album
            track.Album = new Album
            {
                Id = spotifyTrack.Album.Id,
                Name = spotifyTrack.Album.Name,
                Uri = spotifyTrack.Album.Uri,
                Images = spotifyTrack.Album.Images?.Select(img => new Model.Image
                {
                    Url = new Uri(img.Url),
                    Height = img.Height,
                    Width = img.Width
                }).ToArray() ?? Array.Empty<Model.Image>(),
                TotalTracks = spotifyTrack.Album.TotalTracks
            };

            // Set artists
            track.Artists = spotifyTrack.Artists?.Select(artist => new Owner
            {
                Id = artist.Id,
                Name = artist.Name,
                CutName = IOManager.CutString(artist.Name),
                Uri = artist.Uri,
                DisplayName = artist.Name
            }).ToArray() ?? Array.Empty<Owner>();

            return track;
        }

        private async Task FindTracksWithConfidenceAsync(List<Track> spotifyTracks)
        {
            await Task.Run(() =>
            {
                _allMatchResults.Clear();

                foreach (Track track in spotifyTracks)
                {
                    // Use the comprehensive matcher
                    TrackMatchResult? matchResult = ComprehensiveTrackMatcher.FindBestMatch(track, _libraryVM.AudioFiles);

                    if (matchResult != null)
                    {
                        track.IsLocal = true;
                        track.Path = matchResult.AudioFile.Location;
                        track.MatchConfidence = matchResult.Confidence;
                        track.MatchType = matchResult.MatchType;

                        _allMatchResults.Add(matchResult);

                        Debug.WriteLine($"Match found: {track.Title} -> {matchResult.AudioFile.Title} (Confidence: {matchResult.Confidence:F2}, Type: {matchResult.MatchType})");
                    }
                    else
                    {
                        track.IsLocal = false;
                        track.MatchConfidence = 0.0;
                        track.MatchType = "No Match";
                    }
                }
            });
        }

        private string ExtractPlaylistId(string input)
        {
            input = input.Trim();

            // Try to extract from Spotify URL
            Match match = new Regex(@"(?:open\.spotify\.com/playlist/|playlist:)([a-zA-Z0-9]+)", RegexOptions.IgnoreCase)
                .Match(input);

            if (match.Success)
                return match.Groups[1].Value;

            // If it's already just an ID
            if (input.Length >= 20 && input.All(c => char.IsLetterOrDigit(c)))
                return input;

            return string.Empty;
        }

        #endregion

        #region Event Handlers

        private void OnAuthenticationStateChanged(object? sender, bool isAuthenticated)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsAuthenticated = isAuthenticated;
                CurrentUser = _spotifyService.CurrentUserName;

                if (isAuthenticated)
                {
                    StatusMessage = $"Connected as {CurrentUser}";
                    _ = LoadUserPlaylistsAsync();
                }
                else
                {
                    StatusMessage = "Not connected to Spotify";
                    UserPlaylists.Clear();
                    ShowPlaylists = false;
                    SelectedPlaylist = null;
                    ClearCurrentPlaylist();
                    IsNext = false;
                }
            });
        }

        private void LibraryVM_AudioFilesModified(object? sender, EventArgs e)
        {
            if (_allTracks.Any())
            {
                Task.Run(async () =>
                {
                    IsLoading = true;
                    IsNext = false;

                    await FindTracksWithConfidenceAsync(_allTracks);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdatePlaylistStats();
                        ApplyCurrentFilter();

                        if (_allTracks.Any(x => x.IsLocal))
                        {
                            IsNext = true;
                            StatusMessage = $"Re-matched: found {PlaylistFound} tracks ({PerfectMatches} perfect matches)";
                        }

                        IsLoading = false;
                    });
                });
            }
        }

        private void LibraryVM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LibraryVM.AudioFiles) && _allTracks.Any())
            {
                // Re-run matching when library changes
                _ = Task.Run(async () => await FindTracksWithConfidenceAsync(_allTracks));
            }
        }

        partial void OnSelectedPlaylistChanged(PlaylistInfo? value)
        {
            if (value != null)
            {
                _ = LoadPlaylistAsync(value);
            }
        }

        #endregion
    }
}