using Microsoft.Extensions.DependencyInjection;
using NLog;
using SpotifyToM3U.Core;
using SpotifyToM3U.MVVM.Model;
using SpotifyToM3U.MVVM.View.Windows;
using SpotifyToM3U.MVVM.ViewModel;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace SpotifyToM3U
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider _serviceProvider;
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(App));
        private UrlSchemeHandler? _urlSchemeHandler;

        public ServiceProvider ServiceProvider => _serviceProvider;
        internal MainVM MainVM { get; set; } = null!;
        internal IOManager _IOManager = null!;
        internal static new App Current => (App)Application.Current;

        public App()
        {
            SpotifyToM3ULogger.ConfigureNLog();
            _logger.Info("SpotifyToM3U Application starting up");

            try
            {
                IServiceCollection services = new ServiceCollection();
                services.AddSingleton(provider => new MainWindow { DataContext = provider.GetRequiredService<MainVM>() });
                services.AddSingleton(provider => new AddFolderWindow { DataContext = provider.GetRequiredService<AddFolderVM>() });
                services.AddSingleton<MainVM>();
                services.AddSingleton<LibraryVM>();
                services.AddSingleton<SpotifyVM>();
                services.AddSingleton<ExportVM>();
                services.AddSingleton<AddFolderVM>();
                services.AddTransient<SpotifySetupVM>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<ISpotifyService, SpotifyService>();
                services.AddSingleton<IUserSettingsService, UserSettingsService>();
                services.AddSingleton<Func<Type, ViewModelObject>>(provider => viewModelType => (ViewModelObject)provider.GetRequiredService(viewModelType));
                _serviceProvider = services.BuildServiceProvider();

                _logger.Debug("Dependency injection container configured successfully");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Critical error during application initialization");
                throw;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _logger.Info("Application startup initiated");

            try
            {
                base.OnStartup(e);

                // Initialize URL scheme handler for OAuth callbacks
                InitializeUrlSchemeHandler(e.Args);

                if (_urlSchemeHandler != null && !_urlSchemeHandler.IsFirstInstance)
                {
                    if (e.Args.Length > 0 && e.Args[0].StartsWith("spotifytom3u://", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info("Secondary instance detected with OAuth callback URL - forwarding to main instance");
                        _urlSchemeHandler.SendUrlToMainInstance(e.Args[0]);
                    }
                    else
                    {
                        _logger.Info("Secondary instance detected without callback URL - bringing main instance to front");
                    }

                    Shutdown();
                    return;
                }

                // Show window first, then initialize in background
                _serviceProvider.GetRequiredService<INavigationService>().NavigateTo<LibraryVM>();
                MainWindow window = _serviceProvider.GetRequiredService<MainWindow>();
                _IOManager = new IOManager();
                window.Show();

                _logger.Info("Main window displayed successfully");

                // Initialize Spotify service in background
                _ = Task.Run(async () => await InitializeSpotifyServiceAsync());

                if (e.Args.Length > 0 && e.Args[0].StartsWith("spotifytom3u://", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info($"OAuth callback URL received on startup: {e.Args[0]}");
                    _urlSchemeHandler?.ProcessUrl(e.Args[0]);
                }

                _logger.Debug("Background Spotify initialization started");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Critical error during application startup");
                throw;
            }
        }

        /// <summary>
        /// Initializes the URL scheme handler for OAuth callbacks.
        /// </summary>
        private void InitializeUrlSchemeHandler(string[] args)
        {
            try
            {
                _logger.Debug("Initializing URL scheme handler for OAuth");

                _urlSchemeHandler = new UrlSchemeHandler();
                _urlSchemeHandler.Initialize();
                _urlSchemeHandler.UrlReceived += OnOAuthCallbackReceived;
                _logger.Info("URL scheme handler initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize URL scheme handler - OAuth may not work");
            }
        }

        /// <summary>
        /// Handles OAuth callback URLs received via the custom URL scheme.
        /// </summary>
        private void OnOAuthCallbackReceived(string url)
        {
            try
            {
                _logger.Info($"OAuth callback received: {url}");

                if (!url.StartsWith("spotifytom3u://", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn($"Received non-spotifytom3u URL: {url}");
                    return;
                }

                ISpotifyService spotifyService = _serviceProvider.GetRequiredService<ISpotifyService>();
                Dispatcher.Invoke(() =>
                {
                    spotifyService.HandleOAuthCallback(url);
                });
                Dispatcher.Invoke(() =>
                {
                    MainWindow? mainWindow = _serviceProvider.GetService<MainWindow>();
                    if (mainWindow != null)
                    {
                        if (mainWindow.WindowState == WindowState.Minimized)
                        {
                            mainWindow.WindowState = WindowState.Normal;
                        }
                        mainWindow.Activate();
                        mainWindow.Focus();
                        _logger.Debug("Main window brought to front after OAuth callback");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing OAuth callback");
            }
        }

        private async Task InitializeSpotifyServiceAsync()
        {
            _logger.Debug("Starting Spotify service initialization");

            try
            {
                ISpotifyService spotifyService = _serviceProvider.GetRequiredService<ISpotifyService>();
                await spotifyService.InitializeAsync();

                _logger.Info($"Spotify initialization completed. Authenticated: {spotifyService.IsAuthenticated}, User: '{spotifyService.CurrentUserName}'");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Spotify initialization failed");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger.Info("Application shutdown initiated - preserving authentication state");

            try
            {
                _urlSchemeHandler?.Dispose();
                _serviceProvider?.Dispose();
                base.OnExit(e);

                _logger.Info("Application shutdown completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during application shutdown");
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
}