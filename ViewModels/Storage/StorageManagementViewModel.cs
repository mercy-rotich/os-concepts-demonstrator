using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Storage;

public partial class StorageManagementViewModel : ViewModelBase
{
    private readonly StorageService _storageService = new();

    // Incremented on every navigation; lets us ignore results of an outdated request.
    private int _navigationId;

    public ObservableCollection<PhysicalDiskModel> Disks { get; } = new();

    public ObservableCollection<FilesystemGroupModel> Filesystems { get; } = new();

    public ObservableCollection<VirtualFilesystemModel> VirtualFilesystems { get; } = new();

    public ObservableCollection<DirectoryEntryModel> Entries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntryDetails))]
    private DirectoryEntryModel? selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
    private string currentPath = string.Empty;

    [ObservableProperty]
    private string directorySummary = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isDirectoryEmpty;

    [ObservableProperty]
    private bool hasNoFilesystems;

    // One-line totals, with each filesystem counted once.
    [ObservableProperty]
    private string storageSummary = string.Empty;

    [ObservableProperty]
    private string virtualFilesystemsTitle = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string SelectedEntryDetails
    {
        get
        {
            if (SelectedEntry is null)
                return "Select an item to see its details.";

            string details =
                $"{SelectedEntry.FullPath}\n" +
                $"Type: {SelectedEntry.TypeText}   Size: {SelectedEntry.SizeText}   " +
                $"Permissions: {SelectedEntry.Permissions}   Modified: {SelectedEntry.ModifiedText}";

            if (SelectedEntry.LinkTarget is not null)
                details += $"\nLinks to: {SelectedEntry.LinkTarget}";

            return details;
        }
    }

    public StorageManagementViewModel()
    {
        // Fire-and-forget: the page opens immediately and fills in when ready.
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadStorageAsync();

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await NavigateAsync(string.IsNullOrEmpty(home) ? "/" : home);

        // If the home folder could not be read, fall back to the root.
        if (string.IsNullOrEmpty(CurrentPath))
            await NavigateAsync("/");
    }

    private async Task LoadStorageAsync()
    {
        StorageOverviewModel overview = await Task.Run(() => _storageService.GetStorageOverview());

        Disks.Clear();
        Filesystems.Clear();
        VirtualFilesystems.Clear();

        foreach (PhysicalDiskModel disk in overview.Disks)
            Disks.Add(disk);

        foreach (FilesystemGroupModel filesystem in overview.Filesystems)
            Filesystems.Add(filesystem);

        foreach (VirtualFilesystemModel virtualFs in overview.VirtualFilesystems)
            VirtualFilesystems.Add(virtualFs);

        HasNoFilesystems = Filesystems.Count == 0;
        StorageSummary = overview.SummaryText;
        VirtualFilesystemsTitle =
            $"Virtual and other filesystems ({VirtualFilesystems.Count}) - not physical storage";

        if (overview.Error is not null)
            ErrorMessage = overview.Error;
    }

    // Called by the view's "Browse" buttons.
    public void BrowseMountPoint(MountPointModel? mountPoint)
    {
        if (mountPoint is not null)
            _ = NavigateAsync(mountPoint.Path);
    }

    private async Task NavigateAsync(string path)
    {
        int id = ++_navigationId;
        IsLoading = true;

        DirectoryListingModel listing = await Task.Run(() => _storageService.ListDirectory(path));

        // A newer navigation started while we were reading; drop this stale result.
        if (id != _navigationId)
            return;

        IsLoading = false;

        if (listing.Error is not null)
        {
            // Keep showing the previous folder and explain what went wrong.
            ErrorMessage = listing.Error;
            return;
        }

        ErrorMessage = string.Empty;
        CurrentPath = listing.Path;

        Entries.Clear();

        foreach (DirectoryEntryModel entry in listing.Entries)
            Entries.Add(entry);

        SelectedEntry = null;
        IsDirectoryEmpty = Entries.Count == 0;

        DirectorySummary =
            $"{listing.FolderCount} folders, {listing.FileCount} files, " +
            $"{ByteFormatter.Format(listing.TotalFileBytes)} in files (this folder only)" +
            (listing.Truncated ? " - showing the first 2000 items only" : string.Empty);
    }

    // Called by the view when the user double-clicks a row.
    public void OpenEntry(DirectoryEntryModel? entry)
    {
        if (entry is not null && entry.IsDirectory)
            _ = NavigateAsync(entry.FullPath);
    }

    private bool CanGoUp() =>
        !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task GoUpAsync()
    {
        DirectoryInfo? parent = Directory.GetParent(CurrentPath);

        if (parent is not null)
            await NavigateAsync(parent.FullName);
    }

    [RelayCommand]
    private async Task GoHomeAsync()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await NavigateAsync(string.IsNullOrEmpty(home) ? "/" : home);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = string.Empty;

        // Storage is re-read without disturbing the folder being viewed.
        await LoadStorageAsync();

        if (!string.IsNullOrEmpty(CurrentPath))
            await NavigateAsync(CurrentPath);
    }
}
