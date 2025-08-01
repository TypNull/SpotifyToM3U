using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using SpotifyToM3U.Core;
using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace SpotifyToM3U.MVVM.ViewModel
{
    /// <summary>
    /// ViewModel for the Add Folder dialog
    /// </summary>
    internal partial class AddFolderVM : ViewModelObject
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(AddFolderVM));
        [ObservableProperty]
        private string _path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        [ObservableProperty]
        private string _extensions = "mp3,flac,wma,wav,aac,m4a";

        [ObservableProperty]
        private bool _scanSubdirectories = true;

        [ObservableProperty]
        private bool _result = false;

        public event EventHandler? Close;

        public AddFolderVM(INavigationService navigation) : base(navigation)
        {
            _logger.Debug("Initializing AddFolderVM");
            _logger.Info($"AddFolderVM initialized. Default path: {Path}, Extensions: {Extensions}");
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
                    Path = folderBrowser.SelectedPath;
                    _logger.Info($"User selected folder: {Path}");
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

        [RelayCommand]
        private void Scan()
        {
            string folder = Path.Trim();
            _logger.Debug($"Scan command initiated. Folder: {folder}, Extensions: {Extensions}, Subdirectories: {ScanSubdirectories}");

            try
            {
                if (!Directory.Exists(folder))
                {
                    _logger.Warn($"Invalid folder specified: {folder}");
                    System.Windows.MessageBox.Show("Folder '" + folder + "' doesn't exist!", "Invalid folder", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
                else
                {
                    Result = true;
                    _logger.Info($"Scan confirmed. Folder: {folder}, Extensions: {Extensions}, Include subdirectories: {ScanSubdirectories}");
                    Close?.Invoke(this, null!);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in Scan command for folder: {folder}");
            }
        }

        [RelayCommand]
        private void Quit()
        {
            _logger.Debug("Quit command initiated - cancelling folder selection");

            try
            {
                Result = false;
                Close?.Invoke(this, null!);
                _logger.Debug("AddFolder dialog cancelled by user");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in Quit command");
            }
        }
    }
}