using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OS_Concepts_Demonstrator.ViewModels
{
    public class NavigationViewModel : INotifyPropertyChanged
    {
        private object? _currentView;

        public object? CurrentView
        {
            get => _currentView;
            set
            {
                if (_currentView != value)
                {
                    _currentView = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}