using CommunityToolkit.Mvvm.ComponentModel;
using NLog;
using System;
using System.ComponentModel;

namespace SpotifyToM3U.Core
{
    public interface INavigationService : INotifyPropertyChanged
    {
        ViewModelObject CurrentView { get; }
        void NavigateTo<T>() where T : ViewModelObject;
        void NavigateTo(Type type);
    }

    public partial class NavigationService : ObservableObject, INavigationService
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(NavigationService));

        [ObservableProperty]
        private ViewModelObject _currentView = null!;
        private readonly Func<Type, ViewModelObject> _viewModelFactory;


        public NavigationService(Func<Type, ViewModelObject> viewModelFactory)
        {
            _logger.Debug("Initializing NavigationService");
            _viewModelFactory = viewModelFactory;
            _logger.Info("NavigationService initialized successfully");
        }


        public void NavigateTo<TViewModel>() where TViewModel : ViewModelObject
        {
            _logger.Debug($"Navigating to {typeof(TViewModel).Name}");

            try
            {
                ViewModelObject vm = _viewModelFactory.Invoke(typeof(TViewModel));
                CurrentView = vm;
                OnPropertyChanged(nameof(INavigationService.CurrentView));

                _logger.Info($"Successfully navigated to {typeof(TViewModel).Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to navigate to {typeof(TViewModel).Name}");
                throw;
            }
        }

        public void NavigateTo(Type type)
        {
            _logger.Debug($"Navigating to {type.Name}");

            try
            {
                if (!type.IsSubclassOf(typeof(ViewModelObject)))
                {
                    _logger.Error($"Invalid navigation target: {type.FullName} is not a ViewModelObject");
                    throw new NotSupportedException(type.FullName);
                }

                ViewModelObject vm = _viewModelFactory.Invoke(type);
                CurrentView = vm;
                OnPropertyChanged(nameof(INavigationService.CurrentView));

                _logger.Info($"Successfully navigated to {type.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to navigate to {type.Name}");
                throw;
            }
        }
    }
}
