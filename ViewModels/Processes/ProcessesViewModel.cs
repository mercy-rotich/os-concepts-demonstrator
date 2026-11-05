using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Processes;

    public partial class ProcessesViewModel : ViewModelBase
    {
        private readonly ProcessService _processService;


        //all processes retrived from the operating system
        private readonly ObservableCollection<ProcessInfoModel> _allProcesses = new();

        //The collection displayed by the UI.

        public ObservableCollection<ProcessInfoModel> Processes { get; } = new();

        // The text entered by the user in the search box.
        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string sortBy = "Name";

        [ObservableProperty]
        private bool sortDescending;

        public int TotalProcesses => _allProcesses.Count;

        public ProcessesViewModel()
        {
            _processService = new ProcessService();
            RefreshProcesses();
        }

        //called automatically when searchText changes.
        partial void OnSearchTextChanged(string value)
        {
            ApplyFilterAndSort();
        }

        partial void OnSortByChanged(string value)
        {
            ApplyFilterAndSort();
        }

        partial void OnSortDescendingChanged(bool value)
        {
            ApplyFilterAndSort();
        }


        public void RefreshProcesses()
        {
            var runningProcesses = _processService.GetRunningProcesses();
            
            _allProcesses.Clear();

            foreach (ProcessInfoModel process in runningProcesses)
            {
                _allProcesses.Add(process);
            }

            OnPropertyChanged(nameof(TotalProcesses));
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {

            string query = SearchText.Trim();

            var matchingProcesses = _allProcesses.Where(process =>
             process.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
             || process.ProcessId.ToString().Contains(query));

             var sortedProcesses = SortBy switch
            {
                "Memory" => SortDescending
                 ? matchingProcesses.OrderByDescending(p => p.MemoryUsageMb) 
                 : matchingProcesses.OrderBy(p => p.MemoryUsageMb),
                
                "PID" => SortDescending
                 ? matchingProcesses.OrderByDescending(p => p.ProcessId)
                 : matchingProcesses.OrderBy(p => p.ProcessId),
                
                _ => SortDescending
                  ? matchingProcesses.OrderByDescending(p => p.Name)
                  : matchingProcesses.OrderBy(p => p.Name)
            };

            Processes.Clear();

            foreach (ProcessInfoModel process in sortedProcesses)
            {
                Processes.Add(process);
            }
        }
    }