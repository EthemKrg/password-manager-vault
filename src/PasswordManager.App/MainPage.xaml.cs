using System.Collections.ObjectModel;
using PasswordManager.App.Services;
using PasswordManager.Core;

namespace PasswordManager.App;

public partial class MainPage : ContentPage
{
    private readonly IVaultSession _vaultSession;
    private readonly IVaultFilePicker _filePicker;
    private readonly ObservableCollection<AccountEntry> _entries = [];
    private AccountEntry? _selectedEntry;
    private string? _pendingVaultPath;
    private bool _isBusy;

    public MainPage(IVaultSession vaultSession, IVaultFilePicker filePicker)
    {
        InitializeComponent();
        _vaultSession = vaultSession;
        _filePicker = filePicker;
        EntryCollection.ItemsSource = _entries;
        UpdateUi();
    }

    private async void OnCreateVaultClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var masterPassword = CreatePasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(masterPassword))
            {
                SetFeedback("Master password is required.");
                return;
            }

            var vaultPath = await _filePicker.PickCreateVaultPathAsync();
            if (string.IsNullOrWhiteSpace(vaultPath))
            {
                SetFeedback("Vault creation cancelled.");
                return;
            }

            var result = await _vaultSession.CreateAsync(vaultPath, masterPassword);
            CreatePasswordEntry.Text = string.Empty;
            if (!result.Succeeded)
            {
                SetFeedback(Describe(result));
                return;
            }

            _pendingVaultPath = null;
            ClearEntryForm();
            RefreshEntries();
            SetFeedback("Vault created and unlocked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async void OnOpenVaultClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var vaultPath = await _filePicker.PickOpenVaultPathAsync();
            if (string.IsNullOrWhiteSpace(vaultPath))
            {
                SetFeedback("Open cancelled.");
                return;
            }

            _pendingVaultPath = vaultPath;
            UnlockPasswordEntry.Text = string.Empty;
            SetFeedback("Vault selected. Enter the master password.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async void OnUnlockVaultClicked(object? sender, EventArgs e)
    {
        await UnlockSelectedVaultAsync();
    }

    private async void OnUnlockPasswordCompleted(object? sender, EventArgs e)
    {
        await UnlockSelectedVaultAsync();
    }

    private void OnChooseAnotherVaultClicked(object? sender, EventArgs e)
    {
        _pendingVaultPath = null;
        UnlockPasswordEntry.Text = string.Empty;
        SetFeedback("Choose a vault to open.");
        UpdateUi();
    }

    private async void OnSaveVaultClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            await SaveVaultAsync();
        }
        finally
        {
            EndOperation();
        }
    }

    private async void OnLockVaultClicked(object? sender, EventArgs e)
    {
        await LockOrCloseAsync(close: false);
    }

    private async void OnCloseVaultClicked(object? sender, EventArgs e)
    {
        await LockOrCloseAsync(close: true);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshEntries();
    }

    private void OnEntrySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedEntry = e.CurrentSelection.FirstOrDefault() as AccountEntry;
        RenderSelectedEntry();
    }

    private void OnAddNewEntryClicked(object? sender, EventArgs e)
    {
        EntryCollection.SelectedItem = null;
        _selectedEntry = null;
        ClearEntryForm();
        SetFeedback("New entry form ready.");
    }

    private void OnRevealPasswordChanged(object? sender, CheckedChangedEventArgs e)
    {
        PasswordEntry.IsPassword = !e.Value;
    }

    private void OnSaveEntryClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (_selectedEntry is null)
            {
                AddEntryFromForm();
            }
            else
            {
                UpdateEntryFromForm();
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private async void OnDeleteEntryClicked(object? sender, EventArgs e)
    {
        if (_selectedEntry is null || !TryBeginOperation())
        {
            return;
        }

        try
        {
            var confirmed = await DisplayAlertAsync(
                "Delete entry",
                $"Delete {_selectedEntry.ServiceName} from the current snapshot?",
                "Delete",
                "Cancel");
            if (!confirmed)
            {
                SetFeedback("Delete cancelled.");
                return;
            }

            var result = _vaultSession.DeleteEntry(_selectedEntry.Id);
            if (!result.Succeeded)
            {
                SetFeedback(Describe(result));
                return;
            }

            _selectedEntry = null;
            EntryCollection.SelectedItem = null;
            ClearEntryForm();
            RefreshEntries();
            SetFeedback("Entry deleted. Save the vault to persist changes.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task UnlockSelectedVaultAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var masterPassword = UnlockPasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(masterPassword))
            {
                SetFeedback("Master password is required.");
                return;
            }

            var result = _vaultSession.State == VaultSessionState.Locked
                ? await _vaultSession.UnlockCurrentAsync(masterPassword)
                : await _vaultSession.UnlockAsync(_pendingVaultPath ?? string.Empty, masterPassword);
            UnlockPasswordEntry.Text = string.Empty;

            if (!result.Succeeded)
            {
                SetFeedback(Describe(result));
                return;
            }

            _pendingVaultPath = null;
            RefreshEntries();
            SetFeedback("Vault unlocked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LockOrCloseAsync(bool close)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var pendingChoice = await ResolvePendingChangesAsync();
            if (pendingChoice == PendingChangeChoice.Cancel)
            {
                SetFeedback(close ? "Close cancelled." : "Lock cancelled.");
                return;
            }

            var result = close
                ? _vaultSession.Close(discardUnsavedChanges: pendingChoice == PendingChangeChoice.Discard)
                : _vaultSession.Lock(discardUnsavedChanges: pendingChoice == PendingChangeChoice.Discard);
            if (!result.Succeeded)
            {
                SetFeedback(Describe(result));
                return;
            }

            _pendingVaultPath = null;
            _selectedEntry = null;
            EntryCollection.SelectedItem = null;
            ClearEntryForm();
            RefreshEntries();
            SetFeedback(close ? "Vault closed." : "Vault locked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<PendingChangeChoice> ResolvePendingChangesAsync()
    {
        if (!_vaultSession.HasUnsavedChanges)
        {
            return PendingChangeChoice.Continue;
        }

        var action = await DisplayActionSheetAsync(
            "Unsaved changes",
            "Cancel",
            null,
            "Save first",
            "Discard changes");

        if (action == "Save first")
        {
            var saveSucceeded = await SaveVaultAsync();
            return saveSucceeded ? PendingChangeChoice.Continue : PendingChangeChoice.Cancel;
        }

        return action == "Discard changes"
            ? PendingChangeChoice.Discard
            : PendingChangeChoice.Cancel;
    }

    private async Task<bool> SaveVaultAsync()
    {
        var result = await _vaultSession.SaveAsync();
        if (!result.Succeeded)
        {
            SetFeedback(Describe(result));
            UpdateUi();
            return false;
        }

        RefreshEntries();
        SetFeedback("Vault saved.");
        return true;
    }

    private void AddEntryFromForm()
    {
        if (!TryCreateDraftFromForm(out var draft))
        {
            return;
        }

        var result = _vaultSession.AddEntry(draft);
        if (!result.Succeeded)
        {
            SetFeedback(Describe(result));
            return;
        }

        _selectedEntry = result.Value;
        RefreshEntries(selectEntryId: result.Value!.Id);
        SetFeedback("Entry added. Save the vault to persist changes.");
    }

    private void UpdateEntryFromForm()
    {
        if (_selectedEntry is null || !TryCreateDraftFromForm(out var draft))
        {
            return;
        }

        AccountEntry updatedEntry;
        try
        {
            updatedEntry = _selectedEntry with
            {
                ServiceName = draft.ServiceName,
                WebsiteUrl = draft.WebsiteUrl,
                UsernameOrEmail = draft.UsernameOrEmail,
                Password = draft.Password,
                Notes = draft.Notes,
                Tags = draft.Tags,
                IsFavorite = draft.IsFavorite
            };
        }
        catch (ArgumentException ex)
        {
            SetFeedback(ex.Message);
            return;
        }

        var result = _vaultSession.UpdateEntry(updatedEntry);
        if (!result.Succeeded)
        {
            SetFeedback(Describe(result));
            return;
        }

        RefreshEntries(selectEntryId: updatedEntry.Id);
        SetFeedback("Entry updated. Save the vault to persist changes.");
    }

    private bool TryCreateDraftFromForm(out AccountEntryDraft draft)
    {
        draft = null!;

        try
        {
            draft = new AccountEntryDraft(
                ServiceNameEntry.Text ?? string.Empty,
                WebsiteUrlEntry.Text ?? string.Empty,
                UsernameEntry.Text ?? string.Empty,
                PasswordEntry.Text ?? string.Empty,
                NotesEditor.Text ?? string.Empty,
                SplitTags(TagsEntry.Text),
                FavoriteCheckBox.IsChecked);
            return true;
        }
        catch (ArgumentException ex)
        {
            SetFeedback(ex.Message);
            return false;
        }
    }

    private void RefreshEntries(Guid? selectEntryId = null)
    {
        _entries.Clear();

        if (_vaultSession.State != VaultSessionState.Unlocked)
        {
            UpdateUi();
            return;
        }

        var searchResult = _vaultSession.Search(new VaultSearchQuery(SearchEntry.Text ?? string.Empty));
        if (!searchResult.Succeeded)
        {
            SetFeedback(Describe(searchResult));
            UpdateUi();
            return;
        }

        foreach (var entry in searchResult.Value!)
        {
            _entries.Add(entry);
        }

        var selectedId = selectEntryId ?? _selectedEntry?.Id;
        var selectedEntry = selectedId is null
            ? null
            : _entries.FirstOrDefault(entry => entry.Id == selectedId.Value);
        EntryCollection.SelectedItem = selectedEntry;
        _selectedEntry = selectedEntry;

        if (_selectedEntry is null && _entries.Count == 1)
        {
            EntryCollection.SelectedItem = _entries[0];
            _selectedEntry = _entries[0];
        }

        RenderSelectedEntry();
        UpdateUi();
    }

    private void RenderSelectedEntry()
    {
        if (_selectedEntry is null)
        {
            ClearEntryForm();
            return;
        }

        DetailTitleLabel.Text = _selectedEntry.ServiceName;
        DetailUsernameLabel.Text = _selectedEntry.UsernameOrEmail.Length == 0
            ? "No username set"
            : _selectedEntry.UsernameOrEmail;
        ServiceNameEntry.Text = _selectedEntry.ServiceName;
        WebsiteUrlEntry.Text = _selectedEntry.WebsiteUrl;
        UsernameEntry.Text = _selectedEntry.UsernameOrEmail;
        PasswordEntry.Text = _selectedEntry.Password;
        NotesEditor.Text = _selectedEntry.Notes;
        TagsEntry.Text = string.Join(", ", _selectedEntry.Tags);
        FavoriteCheckBox.IsChecked = _selectedEntry.IsFavorite;
        SaveEntryButton.Text = "Update entry";
        DeleteEntryButton.IsEnabled = true;
    }

    private void ClearEntryForm()
    {
        DetailTitleLabel.Text = "New entry";
        DetailUsernameLabel.Text = "Fill the required fields, then add or update.";
        ServiceNameEntry.Text = string.Empty;
        WebsiteUrlEntry.Text = string.Empty;
        UsernameEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        RevealPasswordCheckBox.IsChecked = false;
        PasswordEntry.IsPassword = true;
        FavoriteCheckBox.IsChecked = false;
        NotesEditor.Text = string.Empty;
        TagsEntry.Text = string.Empty;
        SaveEntryButton.Text = "Add entry";
        DeleteEntryButton.IsEnabled = false;
    }

    private void UpdateUi()
    {
        var hasPendingOpen = !string.IsNullOrWhiteSpace(_pendingVaultPath);
        var isUnlocked = _vaultSession.State == VaultSessionState.Unlocked;
        var isLocked = _vaultSession.State == VaultSessionState.Locked;

        StartPanel.IsVisible = !isUnlocked && !isLocked && !hasPendingOpen;
        UnlockPanel.IsVisible = !isUnlocked && (isLocked || hasPendingOpen);
        DashboardPanel.IsVisible = isUnlocked;
        UnsavedBadge.IsVisible = _vaultSession.HasUnsavedChanges;
        SaveVaultButton.IsEnabled = _vaultSession.HasUnsavedChanges;
        EmptyListLabel.IsVisible = isUnlocked && _entries.Count == 0;

        StatusBadge.Text = _vaultSession.State switch
        {
            VaultSessionState.Unlocked => "Unlocked",
            VaultSessionState.Locked => "Locked",
            _ when hasPendingOpen => "Selected",
            _ => "No vault"
        };
        StatusPathLabel.Text = _vaultSession.VaultPath ?? _pendingVaultPath ?? "No vault selected";
        UnlockPathLabel.Text = _vaultSession.VaultPath ?? _pendingVaultPath ?? string.Empty;
        UnlockTitleLabel.Text = isLocked ? "Unlock current vault" : "Unlock selected vault";
    }

    private bool TryBeginOperation()
    {
        if (_isBusy)
        {
            return false;
        }

        _isBusy = true;
        return true;
    }

    private void EndOperation()
    {
        _isBusy = false;
        UpdateUi();
    }

    private void SetFeedback(string message)
    {
        FeedbackLabel.Text = message;
    }

    private static IReadOnlyList<string> SplitTags(string? tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static string Describe(VaultOperationResult result)
    {
        return Describe(result.Error, result.Message);
    }

    private static string Describe<T>(VaultOperationResult<T> result)
    {
        return Describe(result.Error, result.Message);
    }

    private static string Describe(VaultError error, string? message)
    {
        return error switch
        {
            VaultError.InvalidVaultPath => "Vault path is invalid.",
            VaultError.InvalidMasterPassword => "Master password is required.",
            VaultError.InvalidEntry => "Entry data is invalid.",
            VaultError.FileNotFound => "Vault file was not found.",
            VaultError.FileAlreadyExists => "A vault already exists at that path.",
            VaultError.StaleVaultSnapshot => "Vault changed on disk. Reload before saving.",
            VaultError.VaultLocked => "Vault is locked.",
            VaultError.NoVaultLoaded => "No vault is loaded.",
            VaultError.UnsavedChanges => "Save or discard unsaved changes first.",
            VaultError.EntryNotFound => "Entry was not found.",
            VaultError.EntryAlreadyExists => "Entry already exists.",
            VaultError.UnsupportedVaultFormat => "This vault was not created by this app.",
            VaultError.UnsupportedVaultFeatures => "This vault contains unsupported KeePass features.",
            VaultError.OpenFailed => "Vault could not be opened. Check the password.",
            VaultError.SaveFailed => "Vault could not be saved.",
            _ => message ?? "Operation failed."
        };
    }

    private enum PendingChangeChoice
    {
        Continue,
        Discard,
        Cancel
    }
}
