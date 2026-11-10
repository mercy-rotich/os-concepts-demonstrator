using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Processes;

    public partial class ProcessesViewModel : ViewModelBase
    {
        private readonly ProcessService _processService;


        //all processes retrived from the operating system
        private readonly List<ProcessInfoModel> _allProcesses = new();

        //The collection displayed by the UI.

        public ObservableCollection<ProcessInfoModel> Processes { get; } = new();

        // The choices shown in the "Sort by" box. Plain strings, so the selected
        // item is exactly the text the sorting code compares against.
        public IReadOnlyList<string> SortOptions { get; } = new[] { "Name", "Memory", "PID" };

        [ObservableProperty]
        private ProcessInfoModel? selectedProcess;

        // The text entered by the user in the search box.
        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string sortBy = "Name";

        [ObservableProperty]
        private bool sortDescending;

        // True while the operating system is being queried in the background.
        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string errorMessage = string.Empty;

        // True when processes exist but none match the current search.
        [ObservableProperty]
        private bool hasNoMatches;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public int TotalProcesses => _allProcesses.Count;

        public ProcessesViewModel()
        {
            _processService = new ProcessService();

            // Fire-and-forget: the page opens immediately and fills when the scan ends.
            _ = RefreshProcessesAsync();
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


        public async Task RefreshProcessesAsync()
        {
            // Ignore extra clicks while a scan is already running.
            if (IsLoading)
                return;

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                // Reading /proc for every process is slow-ish, so do it off the UI thread.
                List<ProcessInfoModel> runningProcesses =
                    await Task.Run(() => _processService.GetRunningProcesses());

                // Back on the UI thread here (await resumes on Avalonia's context).
                _allProcesses.Clear();
                _allProcesses.AddRange(runningProcesses);

                OnPropertyChanged(nameof(TotalProcesses));
                ApplyFilterAndSort();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not read the process list: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilterAndSort()
        {
            // Remember the selection by PID BEFORE clearing: clearing the list makes
            // the ListBox set SelectedProcess to null.
            int? selectedPid = SelectedProcess?.ProcessId;

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
                  ? matchingProcesses.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                  : matchingProcesses.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            };

            List<ProcessInfoModel> result = sortedProcesses.ToList();

            Processes.Clear();

            foreach (ProcessInfoModel process in result)
            {
                Processes.Add(process);
            }

            // Re-select the same process (the new object carries fresh numbers).
            SelectedProcess = selectedPid is null
                ? null
                : result.FirstOrDefault(p => p.ProcessId == selectedPid);

            HasNoMatches = result.Count == 0 && _allProcesses.Count > 0;
        }
    }
