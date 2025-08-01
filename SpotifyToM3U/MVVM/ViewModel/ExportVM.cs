using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using SpotifyToM3U.Core;
using SpotifyToM3U.MVVM.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpotifyToM3U.MVVM.ViewModel
{
    internal partial class ExportVM : ViewModelObject
    {
        #region Fields

        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(ExportVM));
        private readonly SpotifyVM _spotifyVM;
        private readonly LibraryVM _libraryVM;

        #endregion

        [ObservableProperty]
        private string _exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Playlists\\");
        private string _exportPathOld = string.Empty;
        private string _exportRoot = string.Empty;

        [ObservableProperty]
        private bool _exportAsRelativ = false;

        [ObservableProperty]
        private bool _exportIsVisible = false;

        [ObservableProperty]
        private bool _canAsRelativ = true;

        public ExportVM(INavigationService navigation) : base(navigation)
        {
            _logger.Debug("Initializing ExportVM");

            try
            {
                _spotifyVM = App.Current.ServiceProvider.GetRequiredService<SpotifyVM>();
                _libraryVM = App.Current.ServiceProvider.GetRequiredService<LibraryVM>();
                Navigation.PropertyChanged += Navigation_PropertyChanged;
                PropertyChanged += OnTextBoxPropertyChanged;

                _logger.Info($"ExportVM initialized successfully. Default export path: {ExportPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize ExportVM");
                throw;
            }
        }

        private void Navigation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Navigation.CurrentView))
            {
                ExportIsVisible = false;
                CalculateExportRoot();
            }
        }

        private void CalculateExportRoot()
        {
            Task.Run(() =>
            {
                try
                {
                    List<string?> actualTrackPaths = _spotifyVM.PlaylistTracks
                        .Where(track => track.IsLocal && !string.IsNullOrEmpty(track.Path))
                        .Select(track => Path.GetDirectoryName(track.Path))
                        .Where(dir => !string.IsNullOrEmpty(dir))
                        .Distinct()
                        .ToList();

                    if (actualTrackPaths.Count == 0)
                    {
                        _logger.Debug("No matched tracks found for relative export calculation");
                        return;
                    }

                    _logger.Debug($"Calculating common root from {actualTrackPaths.Count} actual track directories");

                    string commonRoot = FindCommonRoot(actualTrackPaths);
                    if (!string.IsNullOrEmpty(commonRoot) && commonRoot.Length > 3)
                    {
                        _exportRoot = !commonRoot.EndsWith("\\") ? commonRoot + "\\" : commonRoot;
                        CanAsRelativ = true;
                        _logger.Info($"Export root calculated from actual tracks: {_exportRoot}");
                    }
                    else
                    {
                        _logger.Debug($"Common root too generic or empty: '{commonRoot}'");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error calculating export root path");
                }
            });
        }

        private static string FindCommonRoot(List<string?> paths)
        {
            if (paths.Count == 0) return string.Empty;
            if (paths.Count == 1) return paths[0] ?? string.Empty;

            try
            {
                List<string[]> pathSegments = paths.ConvertAll(path => Path.GetFullPath(path!).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

                int minSegments = pathSegments.Min(segments => segments.Length);
                List<string> commonSegments = [];

                for (int i = 0; i < minSegments; i++)
                {
                    string currentSegment = pathSegments[0][i];
                    if (pathSegments.All(segments => segments[i].Equals(currentSegment, StringComparison.OrdinalIgnoreCase)))
                        commonSegments.Add(currentSegment);
                    else
                        break;
                }

                if (commonSegments.Count == 0) return string.Empty;

                string commonPath = string.Join("\\", commonSegments);
                return commonSegments[0].EndsWith(':') ? commonPath + "\\" : "\\" + commonPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetRelativePath(string fullPath, string rootPath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(rootPath))
                return fullPath;

            string normalizedRoot = rootPath.EndsWith("\\") ? rootPath : rootPath + "\\";

            return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath[normalizedRoot.Length..]
                : fullPath;
        }

        private void OnTextBoxPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExportPath))
            {
                _logger.Debug($"Export path changed to: {ExportPath}");

                try
                {
                    ExportIsVisible = false;
                    if (IOManager.TryGetFullPath(ExportPath, out string path))
                    {
                        ExportPath = path;
                        _logger.Info($"Export path validated and normalized to: {path}");
                    }
                    else
                    {
                        _logger.Warn($"Invalid export path specified: {ExportPath}");
                        MessageBox.Show("Invalid Path", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error validating export path: {ExportPath}");
                }
            }
        }

        [RelayCommand]
        private void Relativ()
        {
            _logger.Debug($"Toggling relative export mode. Current state: {ExportAsRelativ}");

            try
            {
                if (ExportAsRelativ)
                {
                    _exportPathOld = ExportPath;
                    ExportPath = _exportRoot;
                    _logger.Info($"Switched to relative export mode. Export path: {ExportPath}");
                }
                else
                {
                    ExportPath = _exportPathOld;
                    _logger.Info($"Switched to absolute export mode. Export path: {ExportPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error toggling relative export mode");
            }
        }

        [RelayCommand]
        private void Export()
        {
            _logger.Info($"Starting export process. Playlist: {_spotifyVM.PlaylistName}, Export path: {ExportPath}");

            try
            {
                Directory.CreateDirectory(ExportPath);

                string playlistFileName = IOManager.RemoveInvalidFileNameChars(_spotifyVM.PlaylistName) + ".m3u8";
                string fullPath = Path.Combine(ExportPath, playlistFileName);

                List<string> files = new()
                {
                    "#EXTM3U",
                    "#" + _spotifyVM.PlaylistName + ".m3u8"
                };

                List<Track> selectedTracks = _spotifyVM.PlaylistTracks.Where(t => t.IsIncludedInExport).ToList();
                _logger.Info($"Exporting {selectedTracks.Count} out of {_spotifyVM.PlaylistTracks.Count} total tracks");

                List<string> exportPaths = selectedTracks.ConvertAll(track =>
                    ExportAsRelativ ? GetRelativePath(track.Path, _exportRoot) : track.Path);

                files.AddRange(exportPaths);
                File.WriteAllLines(fullPath, files);

                _logger.Info($"Playlist exported successfully to: {fullPath}");
                ExportIsVisible = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Export failed for playlist: {_spotifyVM.PlaylistName}");
                System.Windows.MessageBox.Show($"Export error: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void Browse()
        {
            _logger.Debug("Opening folder browser dialog");

            try
            {
                FolderBrowserDialog folderBrowser = new() { ShowNewFolderButton = false };

                if (folderBrowser.ShowDialog() == DialogResult.OK)
                {
                    _logger.Debug($"User selected folder: {folderBrowser.SelectedPath}");

                    if (IOManager.TryGetFullPath(folderBrowser.SelectedPath, out string path))
                    {
                        ExportPath = path;
                        _logger.Info($"Export path updated to: {path}");
                    }
                    else
                    {
                        _logger.Warn($"Invalid folder path selected: {folderBrowser.SelectedPath}");
                        MessageBox.Show("Invalid Path", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    _logger.Debug("Folder browser dialog cancelled by user");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in folder browser dialog");
            }
        }
    }
}