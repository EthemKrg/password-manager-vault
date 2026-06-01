using System.Collections.ObjectModel;
using PasswordManager.App.Services;
using PasswordManager.Core;

namespace PasswordManager.App;

public partial class MainPage : ContentPage
{
    private static readonly TimeSpan ClipboardClearDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PasswordRevealDelay = TimeSpan.FromSeconds(20);

    private readonly IVaultSession _vaultSession;
    private readonly IVaultFilePicker _filePicker;
    private readonly IClipboardService _clipboardService;
    private readonly ObservableCollection<AccountEntry> _entries = [];
    private AccountEntry? _selectedEntry;
    private string? _pendingVaultPath;
    private string? _trackedClipboardText;
    private CancellationTokenSource? _clipboardCountdownCancellation;
    private CancellationTokenSource? _passwordRevealCancellation;
    private readonly HashSet<Button> _hoveredButtons = [];
    private bool _isUpdatingRevealState;
    private bool _isBusy;

    public MainPage(IVaultSession vaultSession, IVaultFilePicker filePicker, IClipboardService clipboardService)
    {
        InitializeComponent();
        _vaultSession = vaultSession;
        _filePicker = filePicker;
        _clipboardService = clipboardService;
        EntryCollection.ItemsSource = _entries;
        AttachButtonHoverHandlers(this);
        UpdateUi();
    }

    protected override void OnDisappearing()
    {
        HidePassword();
        _ = ClearTrackedClipboardAsync(updateStatus: false);
        base.OnDisappearing();
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

    private void OnInputFocused(object? sender, FocusEventArgs e)
    {
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
        if (e.Value)
        {
            StartPasswordRevealAutoHide();
        }
        else
        {
            CancelPasswordRevealAutoHide();
        }
    }

    private async void OnCopyUsernameClicked(object? sender, EventArgs e)
    {
        await CopySensitiveTextAsync(UsernameEntry.Text, "Username");
    }

    private async void OnCopyPasswordClicked(object? sender, EventArgs e)
    {
        await CopySensitiveTextAsync(PasswordEntry.Text, "Password");
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

            HidePassword();
            await ClearTrackedClipboardAsync(updateStatus: true);
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

        HidePassword();
        RefreshEntries();
        SetFeedback("Vault saved.");
        return true;
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
        ApplyButtonState(DeleteEntryButton, pressed: false, focused: DeleteEntryButton.IsFocused, hovered: _hoveredButtons.Contains(DeleteEntryButton));
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
        ApplyButtonState(SaveVaultButton, pressed: false, focused: SaveVaultButton.IsFocused, hovered: _hoveredButtons.Contains(SaveVaultButton));
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
        UpdateUi();
    }

    private void SetFeedback(string message)
    {
        FeedbackLabel.Text = message;
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
}
