using Microsoft.Win32;
using NLog;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyToM3U.Core
{
    public class UrlSchemeHandler : IDisposable
    {
        private const string SchemeName = "spotifytom3u";
        private const string MutexName = "Global\\SpotifyToM3U_SingleInstance";
        private const string PipeName = "SpotifyToM3U_OAuthPipe";
        private const int PipeTimeout = 3000; // 3 seconds

        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(UrlSchemeHandler));

        private Mutex? _mutex;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pipeServerTask;
        private readonly object _eventLock = new object();
        private bool _disposed = false;

        /// <summary>
        /// Event raised when a URL is received via the custom scheme (either in first or subsequent instance).
        /// </summary>
        public event Action<string>? UrlReceived;

        /// <summary>
        /// Indicates if this is the first instance of the application.
        /// </summary>
        public bool IsFirstInstance { get; private set; }

        /// <summary>
        /// Initializes the URL scheme handler, determines if this is the first instance,
        /// and sets up the named pipe server if needed.
        /// </summary>
        public void Initialize()
        {
            try
            {
                _logger.Debug("Initializing UrlSchemeHandler");

                _mutex = new Mutex(true, MutexName, out bool isNew);
                IsFirstInstance = isNew;

                _logger.Info($"Instance check: IsFirstInstance = {IsFirstInstance}");

                if (IsFirstInstance)
                {
                    RegisterUrlScheme();
                    StartPipeServer();

                    _logger.Info("First instance: URL scheme registered and pipe server started");
                }
                else
                {
                    _logger.Info("Not first instance - will forward URLs to existing instance");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize UrlSchemeHandler");
                throw;
            }
        }

        /// <summary>
        /// Sends a URL to the main (first) instance via named pipe.
        /// Should only be called from secondary instances.
        /// </summary>
        /// <param name="url">The URL to send to the main instance</param>
        public void SendUrlToMainInstance(string url)
        {
            if (IsFirstInstance)
            {
                _logger.Warn("SendUrlToMainInstance called on first instance - this shouldn't happen");
                return;
            }

            try
            {
                _logger.Debug($"Sending URL to main instance via named pipe: {url}");

                using NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

                // Connect with timeout
                client.Connect(PipeTimeout);

                using StreamWriter writer = new StreamWriter(client, Encoding.UTF8);
                writer.AutoFlush = true;
                writer.WriteLine(url);

                _logger.Info("URL sent successfully to main instance");
            }
            catch (TimeoutException ex)
            {
                _logger.Error(ex, "Timeout connecting to main instance pipe");
                throw new InvalidOperationException("Could not connect to the main application instance. Please make sure it's running.", ex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send URL to main instance");
                throw;
            }
        }

        /// <summary>
        /// Registers the custom URL scheme (spotifytom3u://) in HKEY_CURRENT_USER registry.
        /// No admin rights required.
        /// </summary>
        private void RegisterUrlScheme()
        {
            try
            {
                string? appPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(appPath))
                {
                    throw new InvalidOperationException("Could not determine application executable path");
                }

                _logger.Debug($"Registering URL scheme '{SchemeName}' with executable: {appPath}");

                using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SchemeName}");
                key.SetValue("", $"URL:{SchemeName} Protocol");
                key.SetValue("URL Protocol", "");

                using RegistryKey iconKey = key.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", $"\"{appPath}\",0");

                using RegistryKey commandKey = key.CreateSubKey(@"shell\open\command");
                commandKey.SetValue("", $"\"{appPath}\" \"%1\"");

                _logger.Info($"URL scheme '{SchemeName}://' registered successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to register URL scheme");
                throw new InvalidOperationException("Failed to register custom URL scheme for OAuth callbacks.", ex);
            }
        }

        /// <summary>
        /// Starts the named pipe server to listen for URLs from secondary instances.
        /// Runs asynchronously on a background thread.
        /// </summary>
        private void StartPipeServer()
        {
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _pipeServerTask = Task.Run(() => PipeServerLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _logger.Debug("Named pipe server task started");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start pipe server");
                throw;
            }
        }

        /// <summary>
        /// Asynchronous loop that continuously listens for incoming URLs via named pipe.
        /// </summary>
        private async Task PipeServerLoopAsync(CancellationToken cancellationToken)
        {
            _logger.Debug("Pipe server loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;

                try
                {
                    pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    _logger.Debug("Waiting for pipe client connection...");
                    await pipeServer.WaitForConnectionAsync(cancellationToken);

                    _logger.Debug("Pipe client connected");

                    using StreamReader reader = new StreamReader(pipeServer, Encoding.UTF8);
                    string? url = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(url))
                    {
                        _logger.Info($"Received URL via pipe: {url}");

                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                lock (_eventLock)
                                {
                                    UrlReceived?.Invoke(url);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Error in UrlReceived event handler");
                            }
                        });
                    }

                    pipeServer.Disconnect();
                }
                catch (OperationCanceledException)
                {
                    _logger.Debug("Pipe server loop cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in pipe server loop");
                    await Task.Delay(1000, cancellationToken);
                }
                finally
                {
                    pipeServer?.Dispose();
                }
            }

            _logger.Debug("Pipe server loop ended");
        }

        /// <summary>
        /// Processes a URL received as a command-line argument and raises the UrlReceived event.
        /// </summary>
        /// <param name="url">The URL to process</param>
        public void ProcessUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            try
            {
                _logger.Info($"Processing URL: {url}");

                lock (_eventLock)
                {
                    UrlReceived?.Invoke(url);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing URL");
            }
        }

        /// <summary>
        /// Disposes resources including mutex and pipe server.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _logger.Debug("Disposing UrlSchemeHandler");

                _cancellationTokenSource?.Cancel();

                _pipeServerTask?.Wait(TimeSpan.FromSeconds(2));

                _cancellationTokenSource?.Dispose();
                _mutex?.Dispose();

                _disposed = true;
                _logger.Debug("UrlSchemeHandler disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing UrlSchemeHandler");
            }
        }
    }
}
