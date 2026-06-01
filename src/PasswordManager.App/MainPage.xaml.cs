using System.Collections.ObjectModel;
using PasswordManager.App.Services;
using PasswordManager.Core;

namespace PasswordManager.App;

public partial class MainPage : ContentPage
{
    private static readonly TimeSpan ClipboardClearDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PasswordRevealDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AutoLockDelay = TimeSpan.FromMinutes(5);
    private const int ExternalPreviewLimit = 25;

    private readonly IVaultSession _vaultSession;
    private readonly IExternalVaultAnalyzer _externalVaultAnalyzer;
    private readonly IVaultFilePicker _filePicker;
    private readonly IClipboardService _clipboardService;
    private readonly IPasswordGenerator _passwordGenerator;
    private readonly IPasswordHealthAnalyzer _passwordHealthAnalyzer;
    private readonly ObservableCollection<AccountEntry> _entries = [];
    private readonly ObservableCollection<BackupArtifactViewModel> _backupArtifacts = [];
    private readonly ObservableCollection<ExternalPreviewViewModel> _externalPreviewEntries = [];
    private PasswordHealthReport _passwordHealthReport = PasswordHealthReport.Empty;
    private AccountEntry? _selectedEntry;
    private BackupArtifactViewModel? _selectedBackupArtifact;
    private string? _pendingVaultPath;
    private string? _trackedClipboardText;
    private CancellationTokenSource? _clipboardCountdownCancellation;
    private CancellationTokenSource? _passwordRevealCancellation;
    private CancellationTokenSource? _autoLockCancellation;
    private readonly HashSet<Button> _hoveredButtons = [];
    private bool _hasExternalAnalysis;
    private bool _isPrivacyMaskActive;
    private bool _isUpdatingRevealState;
    private bool _isBusy;

    public MainPage(
        IVaultSession vaultSession,
        IExternalVaultAnalyzer externalVaultAnalyzer,
        IVaultFilePicker filePicker,
        IClipboardService clipboardService,
        IPasswordGenerator passwordGenerator,
        IPasswordHealthAnalyzer passwordHealthAnalyzer)
    {
        InitializeComponent();
        _vaultSession = vaultSession;
        _externalVaultAnalyzer = externalVaultAnalyzer;
        _filePicker = filePicker;
        _clipboardService = clipboardService;
        _passwordGenerator = passwordGenerator;
        _passwordHealthAnalyzer = passwordHealthAnalyzer;
        EntryCollection.ItemsSource = _entries;
        BackupCollection.ItemsSource = _backupArtifacts;
        ExternalPreviewCollection.ItemsSource = _externalPreviewEntries;
        AttachButtonHoverHandlers(this);
        AttachInputActivityHandlers(this);
        UpdateUi();
    }

    protected override void OnDisappearing()
    {
        HidePassword();
        _ = ClearTrackedClipboardAsync(updateStatus: false);
        base.OnDisappearing();
    }

    public void HandleWindowActivated()
    {
        RecordUserActivity();
    }

    public Task HandleWindowDeactivatedAsync()
    {
        HidePassword();
        return Task.CompletedTask;
    }

    public async Task HandleWindowStoppedAsync()
    {
        await ProtectUnlockedSessionAsync(
            cleanLockFeedback: "Vault locked while the app was in the background.",
            dirtyMaskFeedback: "Vault has unsaved changes. Sensitive content is masked until you save or discard.");
    }

    public async Task HandleWindowDestroyingAsync()
    {
        CancelAutoLockCountdown();
        HidePassword();
        await ClearTrackedClipboardAsync(updateStatus: false);
    }

    private async void OnCreateVaultClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (!RecoveryWarningAcknowledgementCheckBox.IsChecked)
            {
                SetFeedback("Acknowledge the recovery warning before creating a vault.");
                return;
            }

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
            if (!result.Succeeded)
            {
                SetFeedback(Describe(result));
                return;
            }

            _pendingVaultPath = null;
            ClearExternalAnalysis();
            ClearCreateVaultForm();
            ClearEntryForm();
            RefreshEntries();
            await RefreshBackupArtifactsAsync(showFeedback: false);
            SetFeedback("Vault created and unlocked.");
        }
        finally
        {
            CreatePasswordEntry.Text = string.Empty;
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
            ClearCreateVaultForm();
            ClearExternalAnalysis();
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

    private async void OnAnalyzeExternalVaultClicked(object? sender, EventArgs e)
    {
        await AnalyzeSelectedExternalVaultAsync();
    }

    private void OnChooseAnotherVaultClicked(object? sender, EventArgs e)
    {
        _pendingVaultPath = null;
        UnlockPasswordEntry.Text = string.Empty;
        ClearExternalAnalysis();
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

    private async void OnPrivacySaveAndLockClicked(object? sender, EventArgs e)
    {
        await SaveAndLockMaskedSessionAsync();
    }

    private async void OnPrivacyDiscardAndLockClicked(object? sender, EventArgs e)
    {
        await DiscardAndLockMaskedSessionAsync();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RecordUserActivity();
        RefreshEntries();
    }

    private void OnInputFocused(object? sender, FocusEventArgs e)
    {
        RecordUserActivity();
        SetInputFocusState(sender, focused: true);
    }

    private void OnInputUnfocused(object? sender, FocusEventArgs e)
    {
        SetInputFocusState(sender, focused: false);
    }

    private void OnButtonPressed(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonState(button, pressed: true, focused: button.IsFocused, hovered: _hoveredButtons.Contains(button));
        }
    }

    private void OnButtonReleased(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonState(button, pressed: false, focused: button.IsFocused, hovered: _hoveredButtons.Contains(button));
        }
    }

    private void OnButtonFocused(object? sender, FocusEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonState(button, pressed: false, focused: true, hovered: _hoveredButtons.Contains(button));
        }
    }

    private void OnButtonUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonState(button, pressed: false, focused: false, hovered: _hoveredButtons.Contains(button));
        }
    }

    private void OnEntrySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RecordUserActivity();
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
        if (_isUpdatingRevealState)
        {
            return;
        }

        PasswordEntry.IsPassword = !e.Value;
        RecordUserActivity();
        if (e.Value)
        {
            StartPasswordRevealAutoHide();
        }
        else
        {
            CancelPasswordRevealAutoHide();
        }
    }

    private void OnRecoveryAcknowledgementChanged(object? sender, CheckedChangedEventArgs e)
    {
        RecordUserActivity();
        UpdateUi();
    }

    private async void OnCopyUsernameClicked(object? sender, EventArgs e)
    {
        await CopySensitiveTextAsync(UsernameEntry.Text, "Username");
    }

    private async void OnCopyPasswordClicked(object? sender, EventArgs e)
    {
        await CopySensitiveTextAsync(PasswordEntry.Text, "Password");
    }

    private void OnGeneratePasswordClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (_vaultSession.State != VaultSessionState.Unlocked)
            {
                SetFeedback("Unlock a vault before generating an entry password.");
                return;
            }

            if (!int.TryParse(GeneratorLengthEntry.Text, out var length))
            {
                SetFeedback("Password length must be a number.");
                return;
            }

            var options = new PasswordGeneratorOptions(
                length,
                GeneratorUppercaseCheckBox.IsChecked,
                GeneratorLowercaseCheckBox.IsChecked,
                GeneratorDigitsCheckBox.IsChecked,
                GeneratorSymbolsCheckBox.IsChecked);

            HidePassword();
            PasswordEntry.Text = _passwordGenerator.Generate(options);
            SetFeedback("Password generated in memory. Save the entry to persist changes.");
        }
        catch (ArgumentException ex)
        {
            SetFeedback(ex.Message);
        }
        finally
        {
            EndOperation();
        }
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
                $"Delete {_selectedEntry.ServiceName}? This removes it from the current vault snapshot. Save the vault to make the deletion permanent.",
                "Delete entry",
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

            ClearExternalAnalysis();
            var result = _vaultSession.State == VaultSessionState.Locked
                ? await _vaultSession.UnlockCurrentAsync(masterPassword)
                : await _vaultSession.UnlockAsync(_pendingVaultPath ?? string.Empty, masterPassword);
            UnlockPasswordEntry.Text = string.Empty;

            if (!result.Succeeded)
            {
                SetFeedback(IsExternalVaultFailure(result.Error)
                    ? "This vault is not app-managed. Use read-only analysis before any import decision."
                    : Describe(result));
                return;
            }

            _pendingVaultPath = null;
            RefreshEntries();
            await RefreshBackupArtifactsAsync(showFeedback: false);
            SetFeedback("Vault unlocked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task AnalyzeSelectedExternalVaultAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(_pendingVaultPath))
            {
                SetFeedback("Choose an external vault first.");
                return;
            }

            var masterPassword = UnlockPasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(masterPassword))
            {
                SetFeedback("Master password is required to analyze.");
                return;
            }

            var result = await _externalVaultAnalyzer.AnalyzeAsync(_pendingVaultPath, masterPassword);
            UnlockPasswordEntry.Text = string.Empty;

            if (!result.Succeeded)
            {
                ClearExternalAnalysis();
                SetFeedback(Describe(result));
                return;
            }

            RenderExternalAnalysis(result.Value!);
            SetFeedback(result.Value!.Kind switch
            {
                ExternalVaultAnalysisKind.AppManaged => "This is an app-managed vault. Use normal unlock.",
                ExternalVaultAnalysisKind.ExternalReadable when result.Value.CanStrictImport => "External vault analyzed. Strict import candidate.",
                ExternalVaultAnalysisKind.ExternalReadable => "External vault analyzed. Import is blocked until unsupported data is handled.",
                _ => "External vault could not be read by the current provider."
            });
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
            var pendingChoice = await ResolvePendingChangesAsync(close ? "close the vault" : "lock the vault");
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

            HidePassword();
            await ClearTrackedClipboardAsync(updateStatus: true);
            ClearUnlockedUiState();
            SetFeedback(close ? "Vault closed." : "Vault locked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ProtectUnlockedSessionAsync(string cleanLockFeedback, string dirtyMaskFeedback)
    {
        if (_vaultSession.State != VaultSessionState.Unlocked)
        {
            return;
        }

        HidePassword();
        await ClearTrackedClipboardAsync(updateStatus: false);
        RestorePasswordEntry.Text = string.Empty;

        if (_isBusy)
        {
            return;
        }

        if (_vaultSession.HasUnsavedChanges)
        {
            ActivatePrivacyMask(dirtyMaskFeedback);
            return;
        }

        var result = _vaultSession.Lock();
        if (!result.Succeeded)
        {
            ActivatePrivacyMask(Describe(result));
            return;
        }

        ClearUnlockedUiState();
        SetFeedback(cleanLockFeedback);
    }

    private async Task SaveAndLockMaskedSessionAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var saveSucceeded = await SaveVaultAsync();
            if (!saveSucceeded)
            {
                ActivatePrivacyMask("Save failed. Sensitive content remains masked until you save or discard.");
                return;
            }

            var result = _vaultSession.Lock();
            if (!result.Succeeded)
            {
                ActivatePrivacyMask(Describe(result));
                return;
            }

            DeactivatePrivacyMask();
            await ClearTrackedClipboardAsync(updateStatus: true);
            ClearUnlockedUiState();
            SetFeedback("Vault saved and locked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task DiscardAndLockMaskedSessionAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var confirmed = await ConfirmDiscardUnsavedChangesAsync("lock the vault");
            if (!confirmed)
            {
                ActivatePrivacyMask("Discard cancelled. Sensitive content remains masked until you save or discard.");
                return;
            }

            var result = _vaultSession.Lock(discardUnsavedChanges: true);
            if (!result.Succeeded)
            {
                ActivatePrivacyMask(Describe(result));
                return;
            }

            DeactivatePrivacyMask();
            await ClearTrackedClipboardAsync(updateStatus: true);
            ClearUnlockedUiState();
            SetFeedback("Unsaved changes discarded. Vault locked.");
        }
        finally
        {
            EndOperation();
        }
    }

    private void ActivatePrivacyMask(string message)
    {
        _isPrivacyMaskActive = true;
        PrivacyMaskTitleLabel.Text = "Vault protected";
        PrivacyMaskMessageLabel.Text = message;
        UpdateUi();
    }

    private void DeactivatePrivacyMask()
    {
        _isPrivacyMaskActive = false;
        PrivacyMaskMessageLabel.Text = string.Empty;
    }

    private void ClearUnlockedUiState()
    {
        CancelAutoLockCountdown();
        DeactivatePrivacyMask();
        _pendingVaultPath = null;
        _selectedEntry = null;
        _selectedBackupArtifact = null;
        ClearExternalAnalysis();
        EntryCollection.SelectedItem = null;
        BackupCollection.SelectedItem = null;
        RestorePasswordEntry.Text = string.Empty;
        ClearChangeMasterPasswordFields();
        _backupArtifacts.Clear();
        ClearEntryForm();
        RefreshEntries();
    }

    private async Task<PendingChangeChoice> ResolvePendingChangesAsync(string pendingAction)
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

        if (action != "Discard changes")
        {
            return PendingChangeChoice.Cancel;
        }

        var confirmed = await ConfirmDiscardUnsavedChangesAsync(pendingAction);
        return confirmed
            ? PendingChangeChoice.Discard
            : PendingChangeChoice.Cancel;
    }

    private async Task<bool> ConfirmDiscardUnsavedChangesAsync(string pendingAction)
    {
        return await DisplayAlertAsync(
            "Discard unsaved changes",
            $"Discard all unsaved changes and {pendingAction}? Unsaved additions, edits, and deletes will be lost.",
            "Discard changes",
            "Cancel");
    }

    private async Task<bool> SaveVaultAsync()
    {
        var result = await _vaultSession.SaveAsync();
        if (!result.Succeeded)
        {
            await RefreshBackupArtifactsAsync(showFeedback: false);
            SetFeedback(result.Error == VaultError.StaleVaultSnapshot
                ? "Vault changed on disk. A conflict copy is listed under Backups."
                : Describe(result));
            UpdateUi();
            return false;
        }

        HidePassword();
        RefreshEntries();
        await RefreshBackupArtifactsAsync(showFeedback: false);
        SetFeedback("Vault saved.");
        return true;
    }

    private async void OnRefreshBackupsClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            await RefreshBackupArtifactsAsync(showFeedback: true);
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnBackupSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RecordUserActivity();
        _selectedBackupArtifact = e.CurrentSelection.FirstOrDefault() as BackupArtifactViewModel;
        UpdateUi();
    }

    private async void OnRestoreBackupClicked(object? sender, EventArgs e)
    {
        if (_selectedBackupArtifact is null || !TryBeginOperation())
        {
            return;
        }

        try
        {
            var masterPassword = RestorePasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(masterPassword))
            {
                SetFeedback("Master password is required to restore.");
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Restore backup",
                $"Restore {_selectedBackupArtifact.FileName}? The current vault will be backed up first, then replaced by this encrypted backup.",
                "Restore backup",
                "Cancel");
            if (!confirmed)
            {
                SetFeedback("Restore cancelled.");
                return;
            }

            var result = await _vaultSession.RestoreBackupAsync(
                _selectedBackupArtifact.Artifact.FilePath,
                masterPassword);
            RestorePasswordEntry.Text = string.Empty;
            if (!result.Succeeded)
            {
                SetFeedback(Describe(result));
                return;
            }

            HidePassword();
            _selectedEntry = null;
            EntryCollection.SelectedItem = null;
            ClearEntryForm();
            RefreshEntries();
            await RefreshBackupArtifactsAsync(showFeedback: false);
            SetFeedback("Vault restored from encrypted backup.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async void OnChangeMasterPasswordClicked(object? sender, EventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (_vaultSession.State != VaultSessionState.Unlocked)
            {
                SetFeedback("Unlock the vault before changing the master password.");
                return;
            }

            if (_vaultSession.HasUnsavedChanges)
            {
                SetFeedback("Save or discard unsaved changes before changing the master password.");
                return;
            }

            var currentMasterPassword = CurrentMasterPasswordEntry.Text ?? string.Empty;
            var newMasterPassword = NewMasterPasswordEntry.Text ?? string.Empty;
            var confirmNewMasterPassword = ConfirmNewMasterPasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentMasterPassword)
                || string.IsNullOrWhiteSpace(newMasterPassword)
                || string.IsNullOrWhiteSpace(confirmNewMasterPassword))
            {
                SetFeedback("All master password fields are required.");
                return;
            }

            if (!string.Equals(newMasterPassword, confirmNewMasterPassword, StringComparison.Ordinal))
            {
                SetFeedback("New master password confirmation does not match.");
                return;
            }

            if (string.Equals(currentMasterPassword, newMasterPassword, StringComparison.Ordinal))
            {
                SetFeedback("New master password must be different.");
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Change master password",
                "Change the master password for this vault? An encrypted backup will be created first, then the vault will be rewritten. The old master password will no longer unlock the current vault.",
                "Change password",
                "Cancel");
            if (!confirmed)
            {
                SetFeedback("Master password change cancelled.");
                return;
            }

            var result = await _vaultSession.ChangeMasterPasswordAsync(
                currentMasterPassword,
                newMasterPassword);
            if (!result.Succeeded)
            {
                SetFeedback(DescribeMasterPasswordChangeFailure(result));
                return;
            }

            HidePassword();
            await ClearTrackedClipboardAsync(updateStatus: true);
            await RefreshBackupArtifactsAsync(showFeedback: false);
            SetFeedback("Master password changed. Use the new password next time.");
        }
        finally
        {
            ClearChangeMasterPasswordFields();
            EndOperation();
        }
    }

    private async Task CopySensitiveTextAsync(string? text, string label)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (_vaultSession.State != VaultSessionState.Unlocked)
            {
                SetFeedback("Unlock the vault before copying.");
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                SetFeedback($"{label} is empty.");
                return;
            }

            await _clipboardService.SetTextAsync(text);
            StartClipboardCountdown(text);
            SetFeedback($"{label} copied. Clipboard will clear automatically.");
        }
        catch (Exception)
        {
            SetFeedback($"{label} could not be copied.");
        }
        finally
        {
            EndOperation();
        }
    }

    private void StartClipboardCountdown(string copiedText)
    {
        CancelClipboardCountdown(clearTrackedText: false);

        _trackedClipboardText = copiedText;
        _clipboardCountdownCancellation = new CancellationTokenSource();
        ClipboardStatusLabel.IsVisible = true;
        _ = RunClipboardCountdownAsync(copiedText, _clipboardCountdownCancellation.Token);
    }

    private async Task RunClipboardCountdownAsync(string copiedText, CancellationToken cancellationToken)
    {
        try
        {
            for (var remainingSeconds = (int)ClipboardClearDelay.TotalSeconds; remainingSeconds > 0; remainingSeconds--)
            {
                ClipboardStatusLabel.Text = $"Clipboard clears in {remainingSeconds}s";
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var currentClipboard = await _clipboardService.GetTextAsync();
                if (!StringComparer.Ordinal.Equals(currentClipboard, copiedText))
                {
                    ClearClipboardTracking("Clipboard changed; auto-clear cancelled.");
                    return;
                }
            }

            var cleared = await _clipboardService.ClearIfCurrentAsync(copiedText);
            ClearClipboardTracking(cleared ? "Clipboard cleared." : "Clipboard changed; auto-clear cancelled.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ClearClipboardTracking("Clipboard auto-clear failed.");
        }
    }

    private async Task ClearTrackedClipboardAsync(bool updateStatus)
    {
        var trackedText = _trackedClipboardText;
        CancelClipboardCountdown(clearTrackedText: true);

        if (string.IsNullOrEmpty(trackedText))
        {
            return;
        }

        try
        {
            var cleared = await _clipboardService.ClearIfCurrentAsync(trackedText);
            if (updateStatus)
            {
                ClipboardStatusLabel.IsVisible = true;
                ClipboardStatusLabel.Text = cleared ? "Clipboard cleared." : "Clipboard changed; auto-clear cancelled.";
            }
        }
        catch (Exception)
        {
            if (updateStatus)
            {
                ClipboardStatusLabel.IsVisible = true;
                ClipboardStatusLabel.Text = "Clipboard auto-clear failed.";
            }
        }
    }

    private void CancelClipboardCountdown(bool clearTrackedText)
    {
        _clipboardCountdownCancellation?.Cancel();
        _clipboardCountdownCancellation?.Dispose();
        _clipboardCountdownCancellation = null;

        if (clearTrackedText)
        {
            _trackedClipboardText = null;
        }
    }

    private void ClearClipboardTracking(string status)
    {
        CancelClipboardCountdown(clearTrackedText: true);
        ClipboardStatusLabel.IsVisible = true;
        ClipboardStatusLabel.Text = status;
    }

    private void StartPasswordRevealAutoHide()
    {
        CancelPasswordRevealAutoHide();

        _passwordRevealCancellation = new CancellationTokenSource();
        _ = RunPasswordRevealAutoHideAsync(_passwordRevealCancellation.Token);
    }

    private async Task RunPasswordRevealAutoHideAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PasswordRevealDelay, cancellationToken);
            HidePassword(cancelTimer: false);
            SetFeedback("Password hidden.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HidePassword(bool cancelTimer = true)
    {
        if (cancelTimer)
        {
            CancelPasswordRevealAutoHide();
        }

        PasswordEntry.IsPassword = true;
        if (RevealPasswordCheckBox.IsChecked)
        {
            _isUpdatingRevealState = true;
            RevealPasswordCheckBox.IsChecked = false;
            _isUpdatingRevealState = false;
        }
    }

    private void CancelPasswordRevealAutoHide()
    {
        _passwordRevealCancellation?.Cancel();
        _passwordRevealCancellation?.Dispose();
        _passwordRevealCancellation = null;
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

        HidePassword();
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

        HidePassword();
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
            _passwordHealthReport = PasswordHealthReport.Empty;
            UpdateUi();
            return;
        }

        RefreshPasswordHealthReport();
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

    private void RefreshPasswordHealthReport()
    {
        _passwordHealthReport = _vaultSession.CurrentSnapshot is null
            ? PasswordHealthReport.Empty
            : _passwordHealthAnalyzer.Analyze(_vaultSession.CurrentSnapshot);
    }

    private async Task RefreshBackupArtifactsAsync(bool showFeedback)
    {
        _backupArtifacts.Clear();
        _selectedBackupArtifact = null;
        BackupCollection.SelectedItem = null;

        if (_vaultSession.State != VaultSessionState.Unlocked)
        {
            UpdateUi();
            return;
        }

        var result = await _vaultSession.ListBackupArtifactsAsync();
        if (!result.Succeeded)
        {
            if (showFeedback)
            {
                SetFeedback(Describe(result));
            }

            UpdateUi();
            return;
        }

        foreach (var artifact in result.Value!)
        {
            _backupArtifacts.Add(new BackupArtifactViewModel(artifact));
        }

        if (showFeedback)
        {
            SetFeedback(_backupArtifacts.Count == 0
                ? "No backups found."
                : "Backups refreshed.");
        }

        UpdateUi();
    }

    private void RenderExternalAnalysis(ExternalVaultAnalysis analysis)
    {
        _hasExternalAnalysis = true;
        _externalPreviewEntries.Clear();

        foreach (var entry in analysis.Entries.Take(ExternalPreviewLimit))
        {
            _externalPreviewEntries.Add(new ExternalPreviewViewModel(entry));
        }

        ExternalAnalysisTitleLabel.Text = analysis.Kind switch
        {
            ExternalVaultAnalysisKind.AppManaged => "App-managed vault",
            ExternalVaultAnalysisKind.ExternalReadable => "External KeePass preview",
            _ => "Unreadable external vault"
        };
        ExternalAnalysisBadgeLabel.Text = analysis.Kind switch
        {
            ExternalVaultAnalysisKind.AppManaged => "Normal open",
            ExternalVaultAnalysisKind.ExternalReadable when analysis.CanStrictImport => "Strict import ready",
            ExternalVaultAnalysisKind.ExternalReadable => "Import blocked",
            _ => "Read failed"
        };
        ExternalAnalysisSummaryLabel.Text = DescribeExternalAnalysisSummary(analysis);
        ExternalAnalysisIssuesLabel.Text = DescribeExternalIssues(analysis.Issues);

        UpdateUi();
    }

    private void ClearExternalAnalysis()
    {
        _hasExternalAnalysis = false;
        _externalPreviewEntries.Clear();
        ExternalAnalysisTitleLabel.Text = string.Empty;
        ExternalAnalysisBadgeLabel.Text = string.Empty;
        ExternalAnalysisSummaryLabel.Text = string.Empty;
        ExternalAnalysisIssuesLabel.Text = string.Empty;
    }

    private void RenderSelectedEntry()
    {
        if (_selectedEntry is null)
        {
            ClearEntryForm();
            return;
        }

        HidePassword();
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
        RenderPasswordHealthForSelectedEntry();
        ApplyButtonState(DeleteEntryButton, pressed: false, focused: DeleteEntryButton.IsFocused, hovered: _hoveredButtons.Contains(DeleteEntryButton));
    }

    private void ClearEntryForm()
    {
        HidePassword();
        DetailTitleLabel.Text = "New entry";
        DetailUsernameLabel.Text = "Fill the required fields, then add or update.";
        ServiceNameEntry.Text = string.Empty;
        WebsiteUrlEntry.Text = string.Empty;
        UsernameEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        FavoriteCheckBox.IsChecked = false;
        NotesEditor.Text = string.Empty;
        TagsEntry.Text = string.Empty;
        SaveEntryButton.Text = "Add entry";
        DeleteEntryButton.IsEnabled = false;
        PasswordHealthPanel.IsVisible = false;
        PasswordHealthDetailLabel.Text = string.Empty;
        ApplyButtonState(DeleteEntryButton, pressed: false, focused: DeleteEntryButton.IsFocused, hovered: _hoveredButtons.Contains(DeleteEntryButton));
    }

    private void RenderPasswordHealthForSelectedEntry()
    {
        if (_selectedEntry is null)
        {
            PasswordHealthPanel.IsVisible = false;
            PasswordHealthDetailLabel.Text = string.Empty;
            return;
        }

        var issues = _passwordHealthReport
            .GetIssuesForEntry(_selectedEntry.Id)
            .OrderBy(issue => issue.Kind)
            .ToArray();
        PasswordHealthPanel.IsVisible = issues.Length > 0;
        PasswordHealthDetailLabel.Text = issues.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, issues.Select(DescribePasswordHealthIssue));
    }

    private void UpdateUi()
    {
        var hasPendingOpen = !string.IsNullOrWhiteSpace(_pendingVaultPath);
        var isUnlocked = _vaultSession.State == VaultSessionState.Unlocked;
        var isLocked = _vaultSession.State == VaultSessionState.Locked;

        StartPanel.IsVisible = !isUnlocked && !isLocked && !hasPendingOpen;
        UnlockPanel.IsVisible = !isUnlocked && (isLocked || hasPendingOpen);
        DashboardPanel.IsVisible = isUnlocked && !_isPrivacyMaskActive;
        PrivacyMaskPanel.IsVisible = _isPrivacyMaskActive;
        CreateVaultButton.IsEnabled = !isUnlocked
            && !isLocked
            && !hasPendingOpen
            && RecoveryWarningAcknowledgementCheckBox.IsChecked;
        UnsavedBadge.IsVisible = _vaultSession.HasUnsavedChanges;
        SaveVaultButton.IsEnabled = _vaultSession.HasUnsavedChanges;
        PrivacySaveAndLockButton.IsEnabled = isUnlocked && _vaultSession.HasUnsavedChanges;
        PrivacyDiscardAndLockButton.IsEnabled = isUnlocked && _vaultSession.HasUnsavedChanges;
        RefreshBackupsButton.IsEnabled = isUnlocked;
        RestoreBackupButton.IsEnabled = isUnlocked && !_vaultSession.HasUnsavedChanges && _selectedBackupArtifact is not null;
        ChangeMasterPasswordButton.IsEnabled = isUnlocked && !_vaultSession.HasUnsavedChanges;
        EmptyListLabel.IsVisible = isUnlocked && _entries.Count == 0;
        EmptyBackupsLabel.IsVisible = isUnlocked && _backupArtifacts.Count == 0;
        PasswordHealthSummaryLabel.Text = DescribePasswordHealthSummary(isUnlocked);
        PasswordHealthSummaryLabel.TextColor = isUnlocked && _passwordHealthReport.HasIssues
            ? GetResourceColor("WarningText", Colors.DarkGoldenrod)
            : GetResourceColor("Muted", Colors.Gray);
        AnalyzeExternalVaultButton.IsVisible = !isUnlocked && hasPendingOpen && !isLocked;
        AnalyzeExternalVaultButton.IsEnabled = hasPendingOpen && !isLocked;
        ExternalAnalysisDivider.IsVisible = !isUnlocked && _hasExternalAnalysis;
        ExternalAnalysisPanel.IsVisible = !isUnlocked && _hasExternalAnalysis;
        ExternalPreviewCollection.IsVisible = _hasExternalAnalysis && _externalPreviewEntries.Count > 0;
        ApplyButtonState(CreateVaultButton, pressed: false, focused: CreateVaultButton.IsFocused, hovered: _hoveredButtons.Contains(CreateVaultButton));
        ApplyButtonState(SaveVaultButton, pressed: false, focused: SaveVaultButton.IsFocused, hovered: _hoveredButtons.Contains(SaveVaultButton));
        ApplyButtonState(RefreshBackupsButton, pressed: false, focused: RefreshBackupsButton.IsFocused, hovered: _hoveredButtons.Contains(RefreshBackupsButton));
        ApplyButtonState(RestoreBackupButton, pressed: false, focused: RestoreBackupButton.IsFocused, hovered: _hoveredButtons.Contains(RestoreBackupButton));
        ApplyButtonState(ChangeMasterPasswordButton, pressed: false, focused: ChangeMasterPasswordButton.IsFocused, hovered: _hoveredButtons.Contains(ChangeMasterPasswordButton));
        GeneratePasswordButton.IsEnabled = isUnlocked;
        ApplyButtonState(GeneratePasswordButton, pressed: false, focused: GeneratePasswordButton.IsFocused, hovered: _hoveredButtons.Contains(GeneratePasswordButton));
        ApplyButtonState(AnalyzeExternalVaultButton, pressed: false, focused: AnalyzeExternalVaultButton.IsFocused, hovered: _hoveredButtons.Contains(AnalyzeExternalVaultButton));
        ApplyButtonState(PrivacySaveAndLockButton, pressed: false, focused: PrivacySaveAndLockButton.IsFocused, hovered: _hoveredButtons.Contains(PrivacySaveAndLockButton));
        ApplyButtonState(PrivacyDiscardAndLockButton, pressed: false, focused: PrivacyDiscardAndLockButton.IsFocused, hovered: _hoveredButtons.Contains(PrivacyDiscardAndLockButton));
        ApplyButtonState(DeleteEntryButton, pressed: false, focused: DeleteEntryButton.IsFocused, hovered: _hoveredButtons.Contains(DeleteEntryButton));

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
        RecordUserActivity();
        UpdateUi();
    }

    private void SetFeedback(string message)
    {
        FeedbackLabel.Text = message;
    }

    private void ClearChangeMasterPasswordFields()
    {
        CurrentMasterPasswordEntry.Text = string.Empty;
        NewMasterPasswordEntry.Text = string.Empty;
        ConfirmNewMasterPasswordEntry.Text = string.Empty;
    }

    private void ClearCreateVaultForm()
    {
        CreatePasswordEntry.Text = string.Empty;
        RecoveryWarningAcknowledgementCheckBox.IsChecked = false;
    }

    private void SetInputFocusState(object? sender, bool focused)
    {
        if (sender is not VisualElement input)
        {
            return;
        }

        input.BackgroundColor = focused
            ? GetResourceColor("InputFocusSurface", Colors.White)
            : GetResourceColor("SurfaceRaised", Colors.White);
    }

    private static Color GetResourceColor(string key, Color fallback)
    {
        return Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;
    }

    private void RecordUserActivity()
    {
        if (_vaultSession.State != VaultSessionState.Unlocked || _isPrivacyMaskActive)
        {
            return;
        }

        RestartAutoLockCountdown();
    }

    private void RestartAutoLockCountdown()
    {
        CancelAutoLockCountdown();
        _autoLockCancellation = new CancellationTokenSource();
        _ = RunAutoLockCountdownAsync(_autoLockCancellation.Token);
    }

    private async Task RunAutoLockCountdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(AutoLockDelay, cancellationToken);
            await MainThread.InvokeOnMainThreadAsync(() =>
                ProtectUnlockedSessionAsync(
                    cleanLockFeedback: "Vault locked after inactivity.",
                    dirtyMaskFeedback: "Vault has unsaved changes. Sensitive content is masked until you save or discard."));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelAutoLockCountdown()
    {
        _autoLockCancellation?.Cancel();
        _autoLockCancellation?.Dispose();
        _autoLockCancellation = null;
    }

    private static bool IsExternalVaultFailure(VaultError error)
    {
        return error is VaultError.UnsupportedVaultFormat or VaultError.UnsupportedVaultFeatures;
    }

    private static string DescribeExternalAnalysisSummary(ExternalVaultAnalysis analysis)
    {
        return analysis.Kind switch
        {
            ExternalVaultAnalysisKind.AppManaged =>
                "This vault was created by this app. Continue with normal unlock; no import analysis is needed.",
            ExternalVaultAnalysisKind.ExternalReadable when analysis.CanStrictImport =>
                $"{analysis.Entries.Count} entries readable. Password values are hidden; no unsupported features were detected.",
            ExternalVaultAnalysisKind.ExternalReadable =>
                $"{analysis.Entries.Count} entries readable. Password values are hidden; unsupported KeePass data blocks automatic import.",
            _ =>
                "The file could not be opened by the current provider. Wrong password, unsupported KDF, or corruption cannot be reliably distinguished."
        };
    }

    private static string DescribeExternalIssues(IReadOnlyList<ExternalVaultIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "Issues: none.";
        }

        var issueNames = issues
            .Select(issue => DescribeExternalIssueKind(issue.Kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        var suffix = issues.Count > issueNames.Length
            ? $" (+{issues.Count - issueNames.Length} more)"
            : string.Empty;
        return $"Issues: {string.Join(", ", issueNames)}{suffix}.";
    }

    private static string DescribeExternalIssueKind(ExternalVaultIssueKind kind)
    {
        return kind switch
        {
            ExternalVaultIssueKind.Groups => "groups",
            ExternalVaultIssueKind.CustomFields => "custom fields",
            ExternalVaultIssueKind.Attachments => "attachments",
            ExternalVaultIssueKind.History => "history",
            ExternalVaultIssueKind.Icons => "icons",
            ExternalVaultIssueKind.AutoType => "auto-type",
            ExternalVaultIssueKind.UnsupportedMetadata => "metadata",
            ExternalVaultIssueKind.UnsupportedTimestamps => "timestamps",
            ExternalVaultIssueKind.UnsupportedFormatOrKdf => "format/KDF",
            _ => "unsupported data"
        };
    }

    private void AttachInputActivityHandlers(IVisualTreeElement element)
    {
        foreach (var child in element.GetVisualChildren())
        {
            switch (child)
            {
                case Entry entry:
                    entry.TextChanged += (_, _) => RecordUserActivity();
                    break;
                case Editor editor:
                    editor.TextChanged += (_, _) => RecordUserActivity();
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => RecordUserActivity();
                    break;
            }

            if (child is IVisualTreeElement visualChild)
            {
                AttachInputActivityHandlers(visualChild);
            }
        }
    }

    private void AttachButtonHoverHandlers(IVisualTreeElement element)
    {
        foreach (var child in element.GetVisualChildren())
        {
            if (child is Button button)
            {
                var pointer = new PointerGestureRecognizer();
                pointer.PointerEntered += (_, _) =>
                {
                    _hoveredButtons.Add(button);
                    ApplyButtonState(button, pressed: false, focused: button.IsFocused, hovered: true);
                };
                pointer.PointerExited += (_, _) =>
                {
                    _hoveredButtons.Remove(button);
                    ApplyButtonState(button, pressed: false, focused: button.IsFocused, hovered: false);
                };
                button.GestureRecognizers.Add(pointer);
            }

            if (child is IVisualTreeElement visualChild)
            {
                AttachButtonHoverHandlers(visualChild);
            }
        }
    }

    private void ApplyButtonState(Button button, bool pressed, bool focused, bool hovered)
    {
        var tone = GetButtonTone(button);
        var colors = GetButtonColors(tone);

        button.BorderWidth = 1;
        button.BackgroundColor = button.IsEnabled
            ? pressed ? colors.PressedBackground : hovered ? colors.HoverBackground : colors.Background
            : colors.DisabledBackground;
        button.TextColor = button.IsEnabled ? colors.Text : colors.DisabledText;
        button.BorderColor = focused && button.IsEnabled
            ? GetResourceColor("FocusRing", Color.FromArgb("#2C8D6B"))
            : colors.Border;
    }

    private ButtonTone GetButtonTone(Button button)
    {
        if (button.Style is not null && ReferenceEquals(button.Style, Resources["DangerButton"]))
        {
            return ButtonTone.Danger;
        }

        if (button.Style is not null && ReferenceEquals(button.Style, Resources["SecondaryButton"]))
        {
            return ButtonTone.Secondary;
        }

        return ButtonTone.Primary;
    }

    private ButtonColors GetButtonColors(ButtonTone tone)
    {
        return tone switch
        {
            ButtonTone.Secondary => new ButtonColors(
                GetResourceColor("Secondary", Color.FromArgb("#E7EAE2")),
                GetResourceColor("SecondaryHover", Color.FromArgb("#DDE3D7")),
                GetResourceColor("SecondaryPressed", Color.FromArgb("#CED7C8")),
                GetResourceColor("Line", Color.FromArgb("#D5D9D0")),
                GetResourceColor("Ink", Color.FromArgb("#20231F")),
                GetResourceColor("SecondaryDisabled", Color.FromArgb("#EEF0EA")),
                GetResourceColor("MutedDisabled", Color.FromArgb("#90988D"))),
            ButtonTone.Danger => new ButtonColors(
                GetResourceColor("Tertiary", Color.FromArgb("#A94438")),
                GetResourceColor("TertiaryHover", Color.FromArgb("#963B31")),
                GetResourceColor("TertiaryPressed", Color.FromArgb("#7E2F27")),
                GetResourceColor("Tertiary", Color.FromArgb("#A94438")),
                GetResourceColor("White", Color.FromArgb("#FFFFFF")),
                GetResourceColor("TertiaryDisabled", Color.FromArgb("#D8B9B5")),
                GetResourceColor("White", Color.FromArgb("#FFFFFF"))),
            _ => new ButtonColors(
                GetResourceColor("Primary", Color.FromArgb("#1F7A5C")),
                GetResourceColor("PrimaryHover", Color.FromArgb("#1A6B50")),
                GetResourceColor("PrimaryPressed", Color.FromArgb("#14543F")),
                GetResourceColor("Primary", Color.FromArgb("#1F7A5C")),
                GetResourceColor("White", Color.FromArgb("#FFFFFF")),
                GetResourceColor("PrimaryDisabled", Color.FromArgb("#A7B5AB")),
                GetResourceColor("White", Color.FromArgb("#FFFFFF")))
        };
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

    private string DescribePasswordHealthSummary(bool isUnlocked)
    {
        if (!isUnlocked)
        {
            return "Unlock to check password health.";
        }

        if (!_passwordHealthReport.HasIssues)
        {
            return "No password warnings.";
        }

        var entryText = _passwordHealthReport.AffectedEntryCount == 1
            ? "1 entry"
            : $"{_passwordHealthReport.AffectedEntryCount} entries";
        var warningText = _passwordHealthReport.Issues.Count == 1
            ? "1 warning"
            : $"{_passwordHealthReport.Issues.Count} warnings";
        return $"{entryText} need review. {warningText}.";
    }

    private static string DescribePasswordHealthIssue(PasswordHealthIssue issue)
    {
        return issue.Kind switch
        {
            PasswordHealthIssueKind.WeakPassword => "Weak password: use a longer password with mixed character types.",
            PasswordHealthIssueKind.ReusedPassword => issue.RelatedEntryCount <= 2
                ? "Reused password: also used by another entry."
                : $"Reused password: used by {issue.RelatedEntryCount} entries.",
            PasswordHealthIssueKind.OldPassword => "Old password: consider rotating it.",
            _ => "Password needs review."
        };
    }

    private static string DescribeMasterPasswordChangeFailure(VaultOperationResult result)
    {
        return result.Error switch
        {
            VaultError.OpenFailed => "Current master password could not be verified.",
            VaultError.InvalidMasterPassword => "New master password is invalid.",
            VaultError.UnsavedChanges => "Save or discard unsaved changes before changing the master password.",
            VaultError.BackupFailed => "Encrypted backup could not be created. Master password was not changed.",
            VaultError.StaleVaultSnapshot => "Vault changed on disk. Reload before changing the master password.",
            _ => Describe(result)
        };
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
            VaultError.BackupFailed => "Vault backup could not be created. Save was stopped.",
            VaultError.ConflictCopyFailed => "Vault changed on disk and the conflict copy could not be created.",
            VaultError.RestoreFailed => "Vault could not be restored.",
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

    private enum ButtonTone
    {
        Primary,
        Secondary,
        Danger
    }

    private sealed record ButtonColors(
        Color Background,
        Color HoverBackground,
        Color PressedBackground,
        Color Border,
        Color Text,
        Color DisabledBackground,
        Color DisabledText);

    private sealed class BackupArtifactViewModel
    {
        public BackupArtifactViewModel(VaultBackupArtifact artifact)
        {
            Artifact = artifact;
        }

        public VaultBackupArtifact Artifact { get; }

        public string KindLabel => Artifact.Kind == VaultBackupArtifactKind.ConflictCopy
            ? "Conflict"
            : "Backup";

        public string CreatedAtLabel => Artifact.CreatedAtUtc.UtcDateTime.ToString(
            "yyyy-MM-dd HH:mm:ss 'UTC'",
            System.Globalization.CultureInfo.InvariantCulture);

        public string FileName => Path.GetFileName(Artifact.FilePath);
    }

    private sealed class ExternalPreviewViewModel
    {
        public ExternalPreviewViewModel(ExternalVaultPreviewEntry entry)
        {
            ServiceLabel = string.IsNullOrWhiteSpace(entry.ServiceName)
                ? "(no title)"
                : entry.ServiceName;
            UsernameLabel = string.IsNullOrWhiteSpace(entry.UsernameOrEmail)
                ? "No username"
                : entry.UsernameOrEmail;

            var signals = new List<string>
            {
                entry.HasPassword ? "Password present" : "No password",
                entry.HasNotes ? "Notes present" : "No notes"
            };

            if (!string.IsNullOrWhiteSpace(entry.WebsiteUrl))
            {
                signals.Add(entry.WebsiteUrl);
            }

            if (entry.Tags.Count > 0)
            {
                signals.Add($"Tags: {string.Join(", ", entry.Tags)}");
            }

            SignalsLabel = string.Join(" | ", signals);
        }

        public string ServiceLabel { get; }

        public string UsernameLabel { get; }

        public string SignalsLabel { get; }
    }
}
