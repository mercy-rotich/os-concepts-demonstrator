using OS_Concepts_Demonstrator.ViewModels.Dashboard;

namespace OS_Concepts_Demonstrator.ViewModels;

    public class MainViewModel : ViewModelBase
    {
        public DashboardViewModel Dashboard{ get; }

        public MainViewModel()
        {
            Dashboard = new DashboardViewModel();
        }
    }