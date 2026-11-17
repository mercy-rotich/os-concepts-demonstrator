using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        // The details panel reads the Detail... properties below, which are safe when nothing is selected.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        [NotifyPropertyChangedFor(nameof(DetailName))]
        [NotifyPropertyChangedFor(nameof(DetailProcessId))]
        [NotifyPropertyChangedFor(nameof(DetailMemory))]
        [NotifyPropertyChangedFor(nameof(DetailStartTime))]
        [NotifyPropertyChangedFor(nameof(DetailStatus))]
        [NotifyPropertyChangedFor(nameof(DetailPriority))]
        [NotifyPropertyChangedFor(nameof(IsSelectedProcessProtected))]
        [NotifyPropertyChangedFor(nameof(CanSuspend))]
        [NotifyPropertyChangedFor(nameof(CanResume))]
        [NotifyPropertyChangedFor(nameof(CanTerminate))]
        [NotifyPropertyChangedFor(nameof(CanForceKill))]
        [NotifyPropertyChangedFor(nameof(CanChangePriority))]
        private ProcessInfoModel? selectedProcess;

        public bool HasSelection => SelectedProcess is not null;

        public string DetailName => SelectedProcess?.Name ?? string.Empty;

        public string DetailProcessId => SelectedProcess?.ProcessId.ToString() ?? string.Empty;

        public string DetailMemory => SelectedProcess is null ? string.Empty : $"{SelectedProcess.MemoryUsageMb:F2} MB";

        public string DetailStartTime => SelectedProcess is null
            ? string.Empty
            : SelectedProcess.StartTime is DateTime started
                ? started.ToString("dd MMM yyyy, HH:mm:ss")
                : "Unavailable";

        public string DetailStatus => SelectedProcess is null
            ? string.Empty
            : $"{SelectedProcess.StatusText} ({SelectedProcess.StatusCode})";

        public string DetailPriority => SelectedProcess?.NiceValue?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable";

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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasActionMessage))]
        private string actionMessage = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActionMessageColor))]
        private bool isActionError;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSuspend))]
        [NotifyPropertyChangedFor(nameof(CanResume))]
        [NotifyPropertyChangedFor(nameof(CanTerminate))]
        [NotifyPropertyChangedFor(nameof(CanForceKill))]
        [NotifyPropertyChangedFor(nameof(CanChangePriority))]
        private bool isActionBusy;

        [ObservableProperty]
        private bool showTerminateConfirmation;

        [ObservableProperty]
        private bool showForceKillConfirmation;

        [ObservableProperty]
        private string priorityInput = "0";

        // True when processes exist but none match the current search.
        [ObservableProperty]
        private bool hasNoMatches;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

        public string ActionMessageColor => IsActionError ? "#F87171" : "#86EFAC";

        public int TotalProcesses => _allProcesses.Count;

        public bool IsSelectedProcessProtected => SelectedProcess is not null && IsProtectedProcess(SelectedProcess);

        public bool CanSuspend =>
            SelectedProcess is not null
            && !IsActionBusy
            && !IsLoading
            && !IsSelectedProcessProtected
            && SelectedProcess.StatusCode != "T"
            && SelectedProcess.StatusCode != "Z"
            && SelectedProcess.StatusCode != "X";

        public bool CanResume =>
            SelectedProcess is not null
            && !IsActionBusy
            && !IsLoading
            && !IsSelectedProcessProtected
            && SelectedProcess.StatusCode == "T";

        public bool CanTerminate =>
            SelectedProcess is not null
            && !IsActionBusy
            && !IsLoading
            && !IsSelectedProcessProtected
            && SelectedProcess.StatusCode != "Z"
            && SelectedProcess.StatusCode != "X";

        public bool CanForceKill =>
            SelectedProcess is not null
            && !IsActionBusy
            && !IsLoading
            && !IsSelectedProcessProtected
            && SelectedProcess.StatusCode != "Z"
            && SelectedProcess.StatusCode != "X";

        public bool CanChangePriority =>
            SelectedProcess is not null
            && !IsActionBusy
            && !IsLoading
            && !IsSelectedProcessProtected;

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

        partial void OnSelectedProcessChanged(ProcessInfoModel? value)
        {
            ShowTerminateConfirmation = false;
            ShowForceKillConfirmation = false;

            if (value is not null)
            {
                PriorityInput = value.NiceValue?.ToString(CultureInfo.InvariantCulture) ?? "0";
            }
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
                OnPropertyChanged(nameof(CanSuspend));
                OnPropertyChanged(nameof(CanResume));
                OnPropertyChanged(nameof(CanTerminate));
                OnPropertyChanged(nameof(CanForceKill));
                OnPropertyChanged(nameof(CanChangePriority));
            }
        }

        [RelayCommand]
        private async Task SuspendAsync()
        {
            if (!CanSuspend || SelectedProcess is null)
                return;

            await RunActionAsync(() => _processService.SuspendProcess(SelectedProcess));
        }

        [RelayCommand]
        private async Task ResumeAsync()
        {
            if (!CanResume || SelectedProcess is null)
                return;

            await RunActionAsync(() => _processService.ResumeProcess(SelectedProcess));
        }

        [RelayCommand]
        private void AskTerminate()
        {
            if (!CanTerminate)
                return;

            ShowForceKillConfirmation = false;
            ShowTerminateConfirmation = true;
        }

        [RelayCommand]
        private void CancelTerminate()
        {
            ShowTerminateConfirmation = false;
        }

        [RelayCommand]
        private async Task ConfirmTerminateAsync()
        {
            if (!CanTerminate || SelectedProcess is null)
                return;

            ShowTerminateConfirmation = false;
            await RunActionAsync(() => _processService.TerminateProcess(SelectedProcess));
        }

        [RelayCommand]
        private void AskForceKill()
        {
            if (!CanForceKill)
                return;

            ShowTerminateConfirmation = false;
            ShowForceKillConfirmation = true;
        }

        [RelayCommand]
        private void CancelForceKill()
        {
            ShowForceKillConfirmation = false;
        }

        [RelayCommand]
        private async Task ConfirmForceKillAsync()
        {
            if (!CanForceKill || SelectedProcess is null)
                return;

            ShowForceKillConfirmation = false;
            await RunActionAsync(() => _processService.ForceKillProcess(SelectedProcess, confirmed: true));
        }

        [RelayCommand]
        private async Task ChangePriorityAsync()
        {
            if (!CanChangePriority || SelectedProcess is null)
                return;

            if (!int.TryParse(PriorityInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int requestedNice))
            {
                SetActionMessage("Priority must be a whole number between -20 and 19.", isError: true);
                return;
            }

            await RunActionAsync(() => _processService.ChangePriority(SelectedProcess, requestedNice));
        }

        [RelayCommand]
        private async Task RefreshSelectedAsync()
        {
            await RefreshProcessesAsync();
        }

        private async Task RunActionAsync(Func<ProcessActionResult> action)
        {
            if (IsActionBusy)
                return;

            IsActionBusy = true;

            try
            {
                ProcessActionResult result = await Task.Run(action);
                SetActionMessage(result.Message, !result.Success);

                if (result.Success)
                {
                    // Give the kernel a moment to deliver the signal before re-reading /proc.
                    await Task.Delay(300);
                    await RefreshProcessesAsync();
                }
            }
            catch (Exception ex)
            {
                SetActionMessage($"Process action failed: {ex.Message}", isError: true);
            }
            finally
            {
                IsActionBusy = false;
            }
        }

        private void SetActionMessage(string message, bool isError)
        {
            ActionMessage = message;
            IsActionError = isError;
        }

        private static bool IsProtectedProcess(ProcessInfoModel process)
            => ProcessService.IsProtected(process);

        private void ApplyFilterAndSort()
        {
            // Remember the selection by PID BEFORE clearing: clearing the list makes
            // the ListBox set SelectedProcess to null.
            int? selectedPid = SelectedProcess?.ProcessId;
            long? selectedStartTicks = SelectedProcess?.StartTicks;

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
                : result.FirstOrDefault(p => p.ProcessId == selectedPid && p.StartTicks == selectedStartTicks);

            HasNoMatches = result.Count == 0 && _allProcesses.Count > 0;
        }
    }
