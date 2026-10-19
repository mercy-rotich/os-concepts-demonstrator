using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.ViewModels;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Processes;

    public partial class ProcessesViewModel : ViewModelBase
    {
        private readonly ProcessService _processService;

        public ObservableCollection<ProcessInfoModel> Processes { get; set; } = new();

        public ProcessesViewModel()
        {
            _processService = new ProcessService();
            RefreshProcesses();
        }

        public void RefreshProcesses()
        {
            var runningProcesses = _processService.GetRunningProcesses();
            
            Processes.Clear();

            foreach (ProcessInfoModel process in runningProcesses)
            {
                Processes.Add(process);
            }
        }
    }