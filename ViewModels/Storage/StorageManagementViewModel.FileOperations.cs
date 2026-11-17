using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Storage;

// File-management toolbar for the Directory Browser (second half of the partial class).
// The ViewModel only gathers input and shows results; FileOperationsService does the work
// and FileSafetyPolicy decides what is allowed.
public partial class StorageManagementViewModel
{
    private enum NamePromptAction { None, NewFile, NewFolder, Rename }

    private const int MaxSearchResults = 500;

    private readonly FileOperationsService _fileOperations = new();

    private NamePromptAction _pendingNameAction;

    // Captured when Delete is pressed, so the confirmation always refers to that exact item.
    private string? _pendingDeletePath;

    // "Clipboard" for Copy/Move: what to paste, and whether pasting moves it.
    private string? _clipboardPath;
    private bool _clipboardIsMove;

    // Remembered while the overwrite question is shown, so Confirm can repeat the same paste.
    private string? _pendingPasteDestination;

    private CancellationTokenSource? _searchCancellation;

    // ---- state of the current folder ----

    // True when new items may be created here (inside home and not a protected location).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderReadOnly))]
    private bool canModifyCurrentFolder;

    [ObservableProperty]
    private string readOnlyReason = string.Empty;

    public bool IsFolderReadOnly => !CanModifyCurrentFolder && !string.IsNullOrEmpty(CurrentPath);

    // ---- inline name prompt (New File / New Folder / Rename) ----

    [ObservableProperty]
    private bool isNamePromptVisible;

    [ObservableProperty]
    private string namePromptTitle = string.Empty;

    [ObservableProperty]
    private string namePromptInput = string.Empty;

    [ObservableProperty]
    private string namePromptConfirmText = "Create";

    // ---- delete confirmation ----

    [ObservableProperty]
    private bool isDeleteConfirmationVisible;

    [ObservableProperty]
    private string deleteConfirmationText = string.Empty;

    // ---- copy / move clipboard and overwrite confirmation ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboard))]
    private string clipboardText = string.Empty;

    public bool HasClipboard => _clipboardPath is not null;

    [ObservableProperty]
    private bool isOverwriteConfirmationVisible;

    [ObservableProperty]
    private string overwriteConfirmationText = string.Empty;

    // ---- search ----

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private bool isSearchActive;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private string searchSummary = string.Empty;

    private string _activeSearchQuery = string.Empty;

    // ---- result message of the last operation ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperationMessage))]
    private string operationMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationMessageColor))]
    private bool isOperationError;

    public bool HasOperationMessage => !string.IsNullOrEmpty(OperationMessage);

    public string OperationMessageColor => IsOperationError ? "#F87171" : "#86EFAC";

    // ---- properties panel ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPropertiesVisible))]
    private FilePropertiesModel? selectedProperties;

    public bool IsPropertiesVisible => SelectedProperties is not null;

    // ------------------------------------------------------------ reactions

    partial void OnCurrentPathChanged(string value)
    {
        string? reason = string.IsNullOrEmpty(value) ? "No folder open." : _fileOperations.WhyFolderIsReadOnly(value);
        CanModifyCurrentFolder = reason is null;
        ReadOnlyReason = reason ?? string.Empty;

        CloseNamePrompt();
        CloseDeleteConfirmation();
        CloseOverwriteConfirmation();
        SelectedProperties = null;
        NotifyToolbarCommands();
    }

    partial void OnSelectedEntryChanged(DirectoryEntryModel? value)
    {
        // Prompts and panels always refer to one item; close them when the selection moves.
        if (_pendingNameAction == NamePromptAction.Rename)
            CloseNamePrompt();

        if (_pendingDeletePath is not null && _pendingDeletePath != value?.FullPath)
            CloseDeleteConfirmation();

        if (SelectedProperties is not null && SelectedProperties.FullPath != value?.FullPath)
            SelectedProperties = null;

        NotifyToolbarCommands();
    }

    // New File / New Folder / Paste are disabled while search results are shown.
    partial void OnIsSearchActiveChanged(bool value) => NotifyToolbarCommands();

    // Called by NavigateAsync before it loads a folder: stop any search in progress.
    private void OnNavigationStarting()
    {
        _searchCancellation?.Cancel();
        IsSearching = false;
    }

    // Called by NavigateAsync after a folder loaded successfully.
    private void OnNavigationCompleted()
    {
        IsSearchActive = false;
        SearchSummary = string.Empty;
    }

    private void NotifyToolbarCommands()
    {
        NewFileCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        PropertiesCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreateHere() => CanModifyCurrentFolder && !IsSearchActive;

    // Search results can come from sub-folders, so their own location decides (the service re-checks anyway).
    private bool CanActOnSelection() => SelectedEntry is not null && (CanModifyCurrentFolder || IsSearchActive);

    private bool HasSelection() => SelectedEntry is not null;

    private bool CanCopySelection() => SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };

    private bool CanPaste() => HasClipboard && CanModifyCurrentFolder && !IsSearchActive;

    // ------------------------------------------------------------ New File / New Folder / Rename

    [RelayCommand(CanExecute = nameof(CanCreateHere))]
    private void NewFile() => OpenNamePrompt(NamePromptAction.NewFile, "New file name:", "untitled.txt", "Create");

    [RelayCommand(CanExecute = nameof(CanCreateHere))]
    private void NewFolder() => OpenNamePrompt(NamePromptAction.NewFolder, "New folder name:", "New Folder", "Create");

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void Rename()
    {
        if (SelectedEntry is null)
            return;

        OpenNamePrompt(NamePromptAction.Rename, $"Rename '{SelectedEntry.Name}' to:", SelectedEntry.Name, "Rename");
    }

    [RelayCommand]
    private void CancelNamePrompt() => CloseNamePrompt();

    [RelayCommand]
    private async Task ConfirmNamePromptAsync()
    {
        string folder = CurrentPath;
        string name = NamePromptInput;
        string? target = SelectedEntry?.FullPath;
        NamePromptAction action = _pendingNameAction;

        Func<FileOperationResult> operation = action switch
        {
            NamePromptAction.NewFile => () => _fileOperations.CreateFile(folder, name),
            NamePromptAction.NewFolder => () => _fileOperations.CreateDirectory(folder, name),
            NamePromptAction.Rename when target is not null => () => _fileOperations.Rename(target, name),
            _ => () => FileOperationResult.Fail("Nothing to do: select an item first.")
        };

        FileOperationResult result = await Task.Run(operation);
        ShowOperationResult(result);

        // Keep the prompt open on failure so the user can fix the name and try again.
        if (result.Success)
        {
            CloseNamePrompt();
            await ReloadAfterOperationAsync(result.ResultPath);
        }
    }

    // ------------------------------------------------------------ Delete (two steps)

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void Delete()
    {
        if (SelectedEntry is null)
            return;

        _pendingDeletePath = SelectedEntry.FullPath;

        DeleteConfirmationText = SelectedEntry.IsSymbolicLink
            ? $"Delete the symbolic link '{SelectedEntry.Name}'? Only the link is removed, not what it points to ({SelectedEntry.LinkTarget})."
            : SelectedEntry.IsDirectory
                ? $"Delete the folder '{SelectedEntry.Name}'? This only works if it is empty."
                : $"Permanently delete '{SelectedEntry.Name}' ({SelectedEntry.SizeText})? There is no recycle bin: this cannot be undone.";

        CloseNamePrompt();
        IsDeleteConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelDelete() => CloseDeleteConfirmation();

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        string? path = _pendingDeletePath;
        CloseDeleteConfirmation();

        if (path is null)
            return;

        FileOperationResult result = await Task.Run(() => _fileOperations.Delete(path));
        ShowOperationResult(result);

        if (result.Success)
        {
            if (_clipboardPath == path)
                ClearClipboard();

            await ReloadAfterOperationAsync(null);
        }
    }

    // ------------------------------------------------------------ Copy / Move / Paste

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private void Copy() => PutOnClipboard(isMove: false);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void Move() => PutOnClipboard(isMove: true);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync() => await PasteIntoAsync(CurrentPath, overwrite: false);

    [RelayCommand]
    private void CancelClipboard()
    {
        ClearClipboard();
        CloseOverwriteConfirmation();
    }

    [RelayCommand]
    private async Task ConfirmOverwriteAsync()
    {
        string? destination = _pendingPasteDestination;
        CloseOverwriteConfirmation();

        if (destination is not null)
            await PasteIntoAsync(destination, overwrite: true);
    }

    [RelayCommand]
    private void CancelOverwrite()
    {
        CloseOverwriteConfirmation();
        ShowOperationResult(FileOperationResult.Ok("Paste cancelled. Nothing was replaced."));
    }

    private void PutOnClipboard(bool isMove)
    {
        if (SelectedEntry is null)
            return;

        _clipboardPath = SelectedEntry.FullPath;
        _clipboardIsMove = isMove;
        ClipboardText = $"{(isMove ? "Move" : "Copy")}: {SelectedEntry.FullPath}   →   open the destination folder and press Paste here.";
        CloseOverwriteConfirmation();
        NotifyToolbarCommands();
    }

    private async Task PasteIntoAsync(string destination, bool overwrite)
    {
        string? source = _clipboardPath;
        bool isMove = _clipboardIsMove;

        if (source is null)
            return;

        FileOperationResult result = await Task.Run(() => isMove
            ? _fileOperations.Move(source, destination, overwrite)
            : _fileOperations.Copy(source, destination, overwrite));

        if (result.NeedsOverwriteConfirmation)
        {
            _pendingPasteDestination = destination;
            OverwriteConfirmationText = result.Message;
            IsOverwriteConfirmationVisible = true;
            return;
        }

        ShowOperationResult(result);

        if (result.Success)
        {
            // A moved item no longer exists at the old path; a copy can be pasted again elsewhere.
            if (isMove)
                ClearClipboard();

            await ReloadAfterOperationAsync(result.ResultPath);
        }
    }

    private void ClearClipboard()
    {
        _clipboardPath = null;
        ClipboardText = string.Empty;
        NotifyToolbarCommands();
    }

    // ------------------------------------------------------------ Properties

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task PropertiesAsync()
    {
        if (SelectedEntry is null)
            return;

        string path = SelectedEntry.FullPath;
        string error = string.Empty;

        FilePropertiesModel? properties = await Task.Run(() => _fileOperations.GetProperties(path, out error));

        if (properties is null)
        {
            ShowOperationResult(FileOperationResult.Fail($"Could not read the properties of '{Path.GetFileName(path)}': {error}"));
            return;
        }

        // Ignore the result if the user selected something else meanwhile.
        if (SelectedEntry?.FullPath == path)
            SelectedProperties = properties;
    }

    [RelayCommand]
    private void CloseProperties() => SelectedProperties = null;

    // ------------------------------------------------------------ Search

    [RelayCommand]
    private async Task SearchAsync()
    {
        string query = SearchQuery.Trim();
        string start = CurrentPath;

        if (query.Length == 0)
        {
            ShowOperationResult(FileOperationResult.Fail("Type part of a file name to search for."));
            return;
        }

        if (string.IsNullOrEmpty(start))
            return;

        // Cancel an older search and claim a new navigation id, so stale results are dropped.
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        int id = ++_navigationId;

        IsSearching = true;
        SearchSummary = $"Searching for '{query}' in {start} and its sub-folders...";

        SearchOutcome outcome;
        System.Collections.Generic.List<DirectoryEntryModel> rows;

        try
        {
            (outcome, rows) = await Task.Run(() =>
            {
                SearchOutcome found = _fileOperations.Search(start, query, MaxSearchResults, cancellation.Token);
                var built = found.Paths
                    .Select(p => _storageService.DescribeEntry(p, relativeTo: start))
                    .OrderBy(e => e.Location, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return (found, built);
            }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return; // a newer search or a navigation replaced this one
        }

        if (id != _navigationId)
            return;

        IsSearching = false;
        _activeSearchQuery = query;
        IsSearchActive = true;

        Entries.Clear();
        foreach (DirectoryEntryModel row in rows)
            Entries.Add(row);

        SelectedEntry = null;
        IsDirectoryEmpty = false;

        SearchSummary = outcome.Error is not null
            ? outcome.Error
            : rows.Count == 0
                ? $"No names containing '{query}' were found below {start}."
                : $"{rows.Count} result(s) for '{query}' below {start}" +
                  (outcome.Truncated ? $" (stopped at the first {MaxSearchResults})" : string.Empty) +
                  ". Symlinked folders and other filesystems were not entered" +
                  (outcome.SkippedFolders > 0 ? $"; {outcome.SkippedFolders} unreadable folder(s) were skipped." : ".");

        NotifyToolbarCommands();
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        _searchCancellation?.Cancel();
        SearchQuery = string.Empty;
        IsSearching = false;

        if (!string.IsNullOrEmpty(CurrentPath))
            await NavigateAsync(CurrentPath);

        NotifyToolbarCommands();
    }

    // ------------------------------------------------------------ helpers

    // After a change, show fresh contents: re-run the search if results are shown, else reload the folder.
    private async Task ReloadAfterOperationAsync(string? selectPath)
    {
        if (IsSearchActive && _activeSearchQuery.Length > 0)
        {
            SearchQuery = _activeSearchQuery;
            await SearchAsync();

            if (selectPath is not null)
                SelectedEntry = Entries.FirstOrDefault(e => e.FullPath == selectPath);
        }
        else
        {
            await NavigateAsync(CurrentPath, selectPath);
        }
    }

    private void OpenNamePrompt(NamePromptAction action, string title, string suggestion, string confirmText)
    {
        CloseDeleteConfirmation();
        _pendingNameAction = action;
        NamePromptTitle = title;
        NamePromptInput = suggestion;
        NamePromptConfirmText = confirmText;
        IsNamePromptVisible = true;
    }

    private void CloseNamePrompt()
    {
        _pendingNameAction = NamePromptAction.None;
        IsNamePromptVisible = false;
    }

    private void CloseDeleteConfirmation()
    {
        _pendingDeletePath = null;
        IsDeleteConfirmationVisible = false;
    }

    private void CloseOverwriteConfirmation()
    {
        _pendingPasteDestination = null;
        IsOverwriteConfirmationVisible = false;
    }

    private void ShowOperationResult(FileOperationResult result)
    {
        OperationMessage = result.Message;
        IsOperationError = !result.Success;
    }
}
