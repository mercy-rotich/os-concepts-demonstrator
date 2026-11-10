using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.OsServices;

public partial class OsServicesViewModel : ViewModelBase
{
    private const string AllFilter = "All services";
    private const string SystemScopeText = "System services";
    private const string UserScopeText = "My user services";

    private readonly SystemdService _systemd = new();

    // Everything systemd reported; the list below shows the filtered part.
    private readonly List<ServiceInfoModel> _all = new();

    // Increased for every details request, so a slow old answer can not overwrite a newer one.
    private int _detailsRequestId;

    // True while the list is being rebuilt, so the temporary "nothing selected" is ignored.
    private bool _isRebuilding;

    private bool _refreshAgain;

    public ObservableCollection<ServiceInfoModel> Services { get; } = new();

    public ObservableCollection<DetailRow> DetailRows { get; } = new();

    public IReadOnlyList<string> FilterOptions { get; } =
        new[] { AllFilter, "Running", "Exited (finished)", "Failed", "Inactive" };

    public IReadOnlyList<string> ScopeOptions { get; } = new[] { SystemScopeText, UserScopeText };

    // The detail panel reads the Detail... properties below, which are safe when nothing is selected.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(DetailName))]
    [NotifyPropertyChangedFor(nameof(DetailDescription))]
    [NotifyPropertyChangedFor(nameof(DetailStatusText))]
    [NotifyPropertyChangedFor(nameof(DetailStatusExplanation))]
    [NotifyPropertyChangedFor(nameof(DetailStartupExplanation))]
    [NotifyPropertyChangedFor(nameof(DetailIsRunning))]
    [NotifyPropertyChangedFor(nameof(DetailIsFailed))]
    [NotifyPropertyChangedFor(nameof(DetailIsExited))]
    [NotifyPropertyChangedFor(nameof(DetailIsOther))]
    private ServiceInfoModel? selectedService;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedFilter = AllFilter;

    [ObservableProperty]
    private string selectedScope = SystemScopeText;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasNoMatches;

    [ObservableProperty]
    private string lastUpdated = string.Empty;

    // Init system / overall health.
    [ObservableProperty]
    private string managerHeadline = "Reading the service manager...";

    [ObservableProperty]
    private string managerExplanation = string.Empty;

    [ObservableProperty]
    private bool isSystemHealthy = true;

    // Counts for the tiles.
    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int runningCount;

    [ObservableProperty]
    private int exitedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    private int failedCount;

    [ObservableProperty]
    private int inactiveCount;

    // Details of the selected service.
    [ObservableProperty]
    private bool isLoadingDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailsError))]
    private string detailsError = string.Empty;

    [ObservableProperty]
    private string detailLogs = string.Empty;

    public bool HasSelection => SelectedService is not null;

    public string DetailName => SelectedService?.Name ?? string.Empty;

    public string DetailDescription => SelectedService?.Description ?? string.Empty;

    public string DetailStatusText => SelectedService?.StatusText ?? string.Empty;

    public string DetailStatusExplanation => SelectedService?.StatusExplanation ?? string.Empty;

    public string DetailStartupExplanation => SelectedService?.StartupExplanation ?? string.Empty;

    public bool DetailIsRunning => SelectedService?.IsRunning == true;

    public bool DetailIsFailed => SelectedService?.IsFailed == true;

    public bool DetailIsExited => SelectedService?.IsExited == true;

    public bool DetailIsOther => SelectedService?.IsOther == true;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasFailed => FailedCount > 0;

    public bool HasDetailsError => !string.IsNullOrEmpty(DetailsError);

    private ServiceScope CurrentScope =>
        SelectedScope == UserScopeText ? ServiceScope.User : ServiceScope.System;

    public OsServicesViewModel()
    {
        // The page opens at once; the list fills in when systemd has answered.
        _ = RefreshAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(reloadDetails: false);

    partial void OnSelectedFilterChanged(string value) => ApplyFilter(reloadDetails: false);

    partial void OnSelectedScopeChanged(string value)
    {
        // A different scope is a different set of services.
        ClearDetails();
        SelectedService = null;
        _ = RefreshAsync();
    }

    partial void OnSelectedServiceChanged(ServiceInfoModel? value)
    {
        if (_isRebuilding)
            return;

        _ = LoadDetailsAsync(value);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // A refresh requested while one is running is done right after it, not lost.
        if (IsLoading)
        {
            _refreshAgain = true;
            return;
        }

        IsLoading = true;

        try
        {
            do
            {
                _refreshAgain = false;
                await LoadServicesAsync();
            }
            while (_refreshAgain);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadServicesAsync()
    {
        ErrorMessage = string.Empty;

        ServiceScope scope = CurrentScope;
        ServiceManagerInfo manager = await _systemd.GetManagerInfoAsync();

        ManagerHeadline = manager.Headline;
        ManagerExplanation = manager.StateExplanation;
        IsSystemHealthy = manager.IsHealthy || !manager.IsSystemd;

        if (!manager.IsSystemd)
        {
            // Nothing to list on a system without systemd.
            _all.Clear();
            ErrorMessage = manager.StateExplanation;
            UpdateCounts();
            ApplyFilter(reloadDetails: false);
            return;
        }

        (List<ServiceInfoModel> services, string? error) = await _systemd.GetServicesAsync(scope);

        // The user may have switched scope while we waited; drop an answer for the old scope.
        if (scope != CurrentScope)
            return;

        _all.Clear();
        _all.AddRange(services);

        if (error is not null)
            ErrorMessage = error;

        LastUpdated = $"Updated {DateTime.Now:HH:mm:ss}";

        UpdateCounts();
        ApplyFilter(reloadDetails: true);
    }

    private void UpdateCounts()
    {
        TotalCount = _all.Count;
        RunningCount = _all.Count(s => s.IsRunning);
        ExitedCount = _all.Count(s => s.IsExited);
        FailedCount = _all.Count(s => s.IsFailed);
        InactiveCount = _all.Count(s => s.IsInactive);
    }

    private void ApplyFilter(bool reloadDetails)
    {
        string query = SearchText.Trim();

        IEnumerable<ServiceInfoModel> matches = _all.Where(s =>
            (query.Length == 0
                || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || s.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            && MatchesFilter(s));

        List<ServiceInfoModel> result = matches.ToList();

        // Clearing the list makes the ListBox deselect, so remember the selection by name first.
        string? selectedName = SelectedService?.Name;

        _isRebuilding = true;

        try
        {
            Services.Clear();

            foreach (ServiceInfoModel service in result)
                Services.Add(service);

            SelectedService = selectedName is null ? null : result.FirstOrDefault(s => s.Name == selectedName);
        }
        finally
        {
            _isRebuilding = false;
        }

        HasNoMatches = result.Count == 0 && _all.Count > 0;

        if (SelectedService is null)
        {
            if (selectedName is not null)
                ClearDetails();
        }
        else if (reloadDetails)
        {
            // After a refresh the numbers may have changed, so read the details again.
            _ = LoadDetailsAsync(SelectedService);
        }
    }

    private bool MatchesFilter(ServiceInfoModel service) => SelectedFilter switch
    {
        "Running" => service.IsRunning,
        "Exited (finished)" => service.IsExited,
        "Failed" => service.IsFailed,
        "Inactive" => service.IsInactive,
        _ => true
    };

    private async Task LoadDetailsAsync(ServiceInfoModel? service)
    {
        int requestId = ++_detailsRequestId;

        DetailRows.Clear();
        DetailsError = string.Empty;
        DetailLogs = string.Empty;

        if (service is null)
        {
            IsLoadingDetails = false;
            return;
        }

        IsLoadingDetails = true;

        ServiceDetailsModel details = await _systemd.GetDetailsAsync(service.Name, CurrentScope);

        // The user picked something else while we waited: this answer is stale.
        if (requestId != _detailsRequestId)
            return;

        IsLoadingDetails = false;
        DetailsError = details.Error ?? string.Empty;
        DetailLogs = details.Logs;

        foreach (DetailRow row in details.Rows)
            DetailRows.Add(row);
    }

    private void ClearDetails()
    {
        _detailsRequestId++;
        DetailRows.Clear();
        DetailsError = string.Empty;
        DetailLogs = string.Empty;
        IsLoadingDetails = false;
    }
}
