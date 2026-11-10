using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Audit;

public partial class SystemAuditViewModel : ViewModelBase
{
    private readonly AuditService _auditService = new();

    public ObservableCollection<DataSourceStatusModel> Sources { get; } = new();

    public ObservableCollection<DetailRow> IdentityRows { get; } = new();

    public ObservableCollection<LoginSessionModel> Sessions { get; } = new();

    public ObservableCollection<BootRecordModel> Boots { get; } = new();

    public ObservableCollection<LoginSessionModel> CurrentSessions { get; } = new();

    public ObservableCollection<FailedLoginModel> FailedLogins { get; } = new();

    public ObservableCollection<string> AuthLogLines { get; } = new();

    public ObservableCollection<string> KernelMessageLines { get; } = new();

    public ObservableCollection<SecurityCheckModel> ConfigurationChecks { get; } = new();

    public ObservableCollection<SecurityCheckModel> PermissionChecks { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string lastUpdated = string.Empty;

    [ObservableProperty]
    private string historyNote = string.Empty;

    [ObservableProperty]
    private string failedLoginsNote = string.Empty;

    [ObservableProperty]
    private string authLogNote = string.Empty;

    [ObservableProperty]
    private string kernelMessagesNote = string.Empty;

    [ObservableProperty]
    private string configurationSummary = string.Empty;

    [ObservableProperty]
    private string permissionSummary = string.Empty;

    [ObservableProperty]
    private string sourcesSummary = string.Empty;

    public SystemAuditViewModel()
    {
        // The page opens at once and fills in when the read is complete.
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;

        try
        {
            AuditReadout readout = await _auditService.CollectAsync();

            Fill(Sources, readout.Sources);
            Fill(IdentityRows, readout.Identity.Rows);
            Fill(Sessions, readout.History.Sessions);
            Fill(Boots, readout.History.Boots);
            Fill(CurrentSessions, readout.CurrentSessions);
            Fill(FailedLogins, readout.FailedLogins);
            Fill(AuthLogLines, readout.AuthLogLines);
            Fill(KernelMessageLines, readout.KernelMessageLines);
            Fill(ConfigurationChecks, readout.ConfigurationChecks);
            Fill(PermissionChecks, readout.PermissionChecks);

            HistoryNote = readout.History.Error
                ?? (readout.History.Sessions.Count == 0 ? "The login history file is readable but holds no logins." : string.Empty);
            FailedLoginsNote = readout.FailedLoginsReason ?? string.Empty;
            AuthLogNote = readout.AuthLogReason ?? string.Empty;
            KernelMessagesNote = readout.KernelMessagesReason ?? string.Empty;

            ConfigurationSummary = Summarise(readout.ConfigurationChecks);
            PermissionSummary = Summarise(readout.PermissionChecks);

            int available = readout.Sources.Count(s => s.IsAvailable);
            int partial = readout.Sources.Count(s => s.IsPartial);
            int unavailable = readout.Sources.Count(s => s.IsUnavailable);

            SourcesSummary = $"{available} available, {partial} partly available, {unavailable} not available to this account";
            LastUpdated = $"Updated {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string Summarise(System.Collections.Generic.List<SecurityCheckModel> checks) =>
        $"{checks.Count(c => c.IsGood)} as recommended, {checks.Count(c => c.IsReview)} to review, {checks.Count(c => c.IsUnknown)} unknown";

    private static void Fill<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();

        foreach (T item in items)
            target.Add(item);
    }
}
