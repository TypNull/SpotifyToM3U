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
        private SpotifyVM _spotifyVM;
        private LibraryVM _libraryVM;

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
                _libraryVM.AudioFilesModifified += LibraryVM_AudioFilesModifified;
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
            }
        }

        public void LibraryVM_AudioFilesModifified(object? sender, EventArgs e)
        {
            _logger.Debug("Library audio files modified, recalculating export root path");

            try
            {
                if (_libraryVM.RootPathes.Count == 0)
                {
                    _logger.Debug("No root paths available for relative export calculation");
                    return;
                }

                Task.Run(() =>
                {
                    try
                    {
                        string[][] collection = _libraryVM.RootPathes.ToList().Select(x => x.Split("\\")).ToArray();
                        List<string> rootPath = new();

                        for (int j = 0; j < collection[0].Length; j++)
                            if (collection.All(x => x[j] == collection[0][j]))
                                rootPath.Add(collection[0][j]);
                        string path = string.Join("\\", rootPath);
                        if (IOManager.TryGetFullPath(path, out path))
                        {
                            _exportRoot = path + "\\";
                            CanAsRelativ = true;
                            _logger.Info($"Export root path calculated: {_exportRoot}");
                        }
                        else
                        {
                            CanAsRelativ = false;
                            _logger.Warn($"Invalid export root path calculated: {path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error calculating export root path");
                        CanAsRelativ = false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in LibraryVM_AudioFilesModifified handler");
            }
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
                // Create export directory
                Directory.CreateDirectory(ExportPath);
                _logger.Debug($"Export directory created/verified: {ExportPath}");

                // Generate filename
                string playlistFileName = IOManager.RemoveInvalidFileNameChars(_spotifyVM.PlaylistName) + ".m3u8";
                string fullPath = Path.Combine(ExportPath, playlistFileName);
                _logger.Debug($"Full export file path: {fullPath}");

                // Build playlist content
                List<string> files = new()
                {
                    "#EXTM3U",
                    "#" + _spotifyVM.PlaylistName + ".m3u8"
                };

                // Get selected tracks for export
                List<Track> selectedTracks = _spotifyVM.PlaylistTracks.Where(t => t.IsIncludedInExport).ToList();
                _logger.Info($"Exporting {selectedTracks.Count} out of {_spotifyVM.PlaylistTracks.Count} total tracks");

                List<string> exportPaths = selectedTracks.Select(track =>
                    ExportAsRelativ ? track.Path.Remove(0, _exportRoot.Length) : track.Path).ToList();

                files.AddRange(exportPaths);

                // Write the playlist file
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
                FolderBrowserDialog folderBrowser = new()
                {
                    ShowNewFolderButton = false
                };

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
