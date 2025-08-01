using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Requests;
using SpotifyToM3U.Core;
using SpotifyToM3U.MVVM.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace SpotifyToM3U.MVVM.ViewModel
{
    internal partial class LibraryVM : ViewModelObject
    {
        #region Fields

        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(LibraryVM));

        #endregion

        #region Properties

        public AudioFileCollection AudioFiles
        {
            get => _audioFiles;
            set => SetProperty(ref _audioFiles, value);
        }
        private AudioFileCollection _audioFiles = new();

        [ObservableProperty]
        private bool _isNext = false;

        [ObservableProperty]
        private bool _showProgressBar = false;

        [ObservableProperty]
        private float _progressValue = 0f;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private int _totalFilesCount = 0;

        [ObservableProperty]
        private int _processedFilesCount = 0;

        private long _processedFiles = 0;
        private long _totalFiles = 0;
        private CancellationTokenSource? _cancellationTokenSource;

        public BlockingCollection<string> RootPathes { get; set; } = new();

        public RequestHandler RequestHandler { get; } = new()
        {
            StaticDegreeOfParallelism = Math.Max(Environment.ProcessorCount / 2, 1) // More conservative CPU usage
        };

        #endregion

        #region Events

        public event EventHandler? AudioFilesModifified;

        #endregion

        #region Constructor

        public LibraryVM(INavigationService navigation) : base(navigation)
        {
            _logger.Debug("Initializing LibraryVM");

            try
            {
                BindingOperations.EnableCollectionSynchronization(AudioFiles, new object());
                StatusText = $"Audio Library - {AudioFiles.Count} files loaded";

                _logger.Info("LibraryVM initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize LibraryVM");
                throw;
            }
        }

        #endregion

        #region Commands

        [RelayCommand]
        private async Task AddFolderAsync()
        {
            _logger.Debug("AddFolderAsync command initiated");

            try
            {
                View.Windows.AddFolderWindow addFolderWindow = App.Current.ServiceProvider.GetRequiredService<View.Windows.AddFolderWindow>();
                AddFolderVM vm = (AddFolderVM)addFolderWindow.DataContext;

                addFolderWindow.ShowDialog();
                if (!vm.Result)
                {
                    _logger.Debug("AddFolder dialog cancelled by user");
                    return;
                }

                _logger.Info($"Adding folder: {vm.Path} (Extensions: {vm.Extensions}, Subdirectories: {vm.ScanSubdirectories})");
                await ProcessFolderAsync(vm.Path, vm.Extensions, vm.ScanSubdirectories);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AddFolderAsync command");
                throw;
            }
        }

        [RelayCommand]
        private async Task AddFileAsync()
        {
            _logger.Debug("AddFileAsync command initiated");

            try
            {
                Microsoft.Win32.OpenFileDialog fileDialog = new()
                {
                    Multiselect = true,
                    Title = "Select Audio Files",
                    Filter = "Audio Files (*.MP3, *.FLAC, *.WMA, *.WAV, *.AAC, *.M4A)|*.MP3;*.FLAC;*.aac;*m4a;*.WMA;*.WAV|All Files (*.*)|*.*"
                };

                if (fileDialog.ShowDialog() != true)
                {
                    _logger.Debug("AddFile dialog cancelled by user");
                    return;
                }

                _logger.Info($"Adding {fileDialog.FileNames.Length} files");
                await ProcessFilesAsync(fileDialog.FileNames);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AddFileAsync command");
                throw;
            }
        }

        [RelayCommand]
        private void Clear()
        {
            _logger.Debug("Clear command initiated");

            try
            {
                _cancellationTokenSource?.Cancel();

                int clearedCount = AudioFiles.Count;
                AudioFiles.Clear();
                IsNext = false;
                TotalFilesCount = 0;
                ProcessedFilesCount = 0;
                StatusText = "Library cleared";

                while (RootPathes.TryTake(out _)) { }
                AudioFilesModifified?.Invoke(this, EventArgs.Empty);

                _logger.Info($"Library cleared - removed {clearedCount} files");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error clearing library");
                throw;
            }
        }

        [RelayCommand]
        private void Spotify() => Navigation.NavigateTo<SpotifyVM>();

        [RelayCommand]
        private void CancelOperation()
        {
            _cancellationTokenSource?.Cancel();
            StatusText = "Operation cancelled";
        }

        #endregion

        #region Private Methods

        private async Task ProcessFolderAsync(string folderPath, string extensions, bool scanSubdirectories)
        {
            _logger.Debug($"Starting folder processing: {folderPath}");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            try
            {
                IsNext = false;
                ShowProgressBar = true;
                StatusText = "Scanning folder...";

                string[] exts = extensions.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(x => x.ToLower().Trim())
                                   .ToArray();

                _logger.Debug($"Processing extensions: [{string.Join(", ", exts)}], Subdirectories: {scanSubdirectories}");

                int initialCount = AudioFiles.Count;
                RootPathes.Add(folderPath);

                await LoadFolderFilesAsync(exts, folderPath, scanSubdirectories, token);

                int addedFiles = AudioFiles.Count - initialCount;
                IsNext = AudioFiles.Count > 0;
                StatusText = $"Added {addedFiles} files. Total: {AudioFiles.Count} files";

                if (!token.IsCancellationRequested)
                {
                    _logger.Info($"Folder scan completed successfully - added {addedFiles} files from {folderPath}");
                    MessageBox.Show($"{addedFiles} files added successfully!", "Scan Complete",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }

                AudioFilesModifified?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Scan cancelled";
                _logger.Info($"Folder scan cancelled by user: {folderPath}");
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                _logger.Error(ex, $"Error scanning folder: {folderPath}");
                MessageBox.Show($"Error scanning folder: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowProgressBar = false;
                ResetProgress();
                _logger.Debug($"Folder processing completed: {folderPath}");
            }
        }

        private async Task ProcessFilesAsync(string[] filePaths)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            try
            {
                IsNext = false;
                ShowProgressBar = true;
                _totalFiles = filePaths.Length;
                TotalFilesCount = (int)_totalFiles;
                StatusText = "Processing files...";

                await Task.Run(async () =>
                {
                    RequestContainer<OwnRequest> container = new();

                    foreach (string path in filePaths)
                    {
                        if (token.IsCancellationRequested) break;

                        container.Add(new OwnRequest(async (t) =>
                        {
                            if (!AudioFiles.ContainsFile(path))
                            {
                                AudioFile audioFile = new(path);
                                Application.Current.Dispatcher.Invoke(() => AudioFiles.Add(audioFile));
                                RootPathes.Add(path);
                            }

                            long processed = Interlocked.Increment(ref _processedFiles);
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                ProcessedFilesCount = (int)processed;
                                ProgressValue = (float)processed / _totalFiles;
                            });

                            return true;
                        }, new() { Handler = RequestHandler }));
                    }

                    await container.Task;
                }, token);

                IsNext = AudioFiles.Count > 0;
                StatusText = $"Files processed. Total: {AudioFiles.Count} files";
                AudioFilesModifified?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Processing cancelled";
            }
            finally
            {
                ShowProgressBar = false;
                ResetProgress();
            }
        }

        private async Task LoadFolderFilesAsync(string[] exts, string root, bool recursive, CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                IEnumerable<FileInfo> files = GetFiles(new DirectoryInfo(root),
                    "\\." + string.Join("|\\.", exts),
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                _totalFiles = files.Count();
                TotalFilesCount = (int)_totalFiles;

                if (_totalFiles <= 0)
                {
                    StatusText = "No audio files found";
                    return;
                }

                StatusText = $"Found {_totalFiles} files, processing...";

                RequestContainer<OwnRequest> container = new();
                container.Add(new OwnRequest(async (t) =>
                {
                    await ScanFolderAsync(root, recursive, exts, container, token);
                    return true;
                }, new() { Handler = RequestHandler }));

                await Task.Delay(100, token);

                while (container.State == Requests.Options.RequestState.Running && !token.IsCancellationRequested)
                {
                    await Task.Delay(200, token);
                }

                await container.Task;
            }
            finally
            {
                stopwatch.Stop();
                Debug.WriteLine($"Folder scan completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            }
        }

        private async Task ScanFolderAsync(string folder, bool recurse, string[] extensions,
            RequestContainer<OwnRequest> container, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                DirectoryInfo di = new(folder);
                IEnumerable<FileInfo> files = GetFiles(di, "\\." + string.Join("|\\.", extensions));

                IEnumerable<Task> tasks = files.Select(async fi =>
                {
                    if (token.IsCancellationRequested) return;

                    if (!AudioFiles.ContainsFile(fi.FullName))
                    {
                        AudioFile audioFile = new(fi.FullName);
                        await Application.Current.Dispatcher.InvokeAsync(() => AudioFiles.Add(audioFile));
                    }

                    long processed = Interlocked.Increment(ref _processedFiles);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProcessedFilesCount = (int)processed;
                        ProgressValue = (float)processed / _totalFiles;
                        StatusText = $"Processing: {Path.GetFileName(fi.FullName)}";
                    });
                });

                await Task.WhenAll(tasks);

                if (!recurse || token.IsCancellationRequested) return;

                foreach (DirectoryInfo subDir in di.EnumerateDirectories())
                {
                    if (token.IsCancellationRequested) break;

                    container.Add(new OwnRequest(async (t) =>
                    {
                        await ScanFolderAsync(subDir.FullName, recurse, extensions, container, token);
                        return true;
                    }, new() { Handler = RequestHandler }));
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Debug.WriteLine($"Error scanning folder {folder}: {ex.Message}");
            }
        }

        private static IEnumerable<FileInfo> GetFiles(DirectoryInfo directory,
            string searchPatternExpression = "",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            try
            {
                Regex reSearchPattern = new(searchPatternExpression, RegexOptions.IgnoreCase);
                return directory.EnumerateFiles("*", searchOption)
                               .Where(file => reSearchPattern.IsMatch(file.Extension));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enumerating files: {ex.Message}");
                return Enumerable.Empty<FileInfo>();
            }
        }

        private void ResetProgress()
        {
            _processedFiles = 0;
            ProgressValue = 0;
            ProcessedFilesCount = 0;
            TotalFilesCount = 0;
        }

        #endregion
    }
}