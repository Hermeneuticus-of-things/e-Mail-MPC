using System.Windows;
using System.Windows.Threading;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class ComposeWindow
{
    private readonly DispatcherTimer _draftAutosaveTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private readonly SemaphoreSlim _draftSaveGate = new(1, 1);
    private bool _isInitializing = true;
    private bool _hasUnsavedDraftChanges;
    private bool _isSavingBeforeClose;
    private int _draftRevision;

    private void InitializeDraftAutosave()
    {
        _draftAutosaveTimer.Tick += DraftAutosaveTimer_Tick;
    }

    private void CompleteDraftInitialization(MessageResponseDraft? response)
    {
        _isInitializing = false;
        if (response is not null)
        {
            MarkDraftDirty();
        }
    }

    private void ComposeField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        MarkDraftDirty();
    }

    private void MarkDraftDirty()
    {
        if (_isInitializing || _isCompleting)
        {
            return;
        }

        _draftRevision++;
        _hasUnsavedDraftChanges = true;
        StatusText.Text = "Draft changes will save automatically.";
        _draftAutosaveTimer.Stop();
        _draftAutosaveTimer.Start();
    }

    private async void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        _draftAutosaveTimer.Stop();
        StatusText.Text = "Saving draft in the encrypted local store...";
        try
        {
            await PersistDraftAsync();
            StatusText.Text = _hasUnsavedDraftChanges
                ? "More changes were made while saving; they will save automatically."
                : "Draft saved locally with its attachments.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Draft could not be saved.";
            ShowMessageBox(
                this,
                $"The draft could not be saved to the encrypted local store.\n\n{exception.Message}",
                "Draft save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DraftAutosaveTimer_Tick(object? sender, EventArgs e)
    {
        _draftAutosaveTimer.Stop();
        if (!_hasUnsavedDraftChanges || _isInitializing || _isCompleting)
        {
            return;
        }

        if (_isBusy)
        {
            _draftAutosaveTimer.Start();
            return;
        }

        StatusText.Text = "Saving draft automatically...";
        try
        {
            await PersistDraftAsync();
            StatusText.Text = _hasUnsavedDraftChanges
                ? "Saving the latest draft changes..."
                : "Draft saved automatically.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Automatic save failed. Keep this window open or choose Save draft to retry. {exception.Message}";
        }
    }

    private async Task PersistDraftAsync()
    {
        await _draftSaveGate.WaitAsync();
        try
        {
            var revision = _draftRevision;
            _draftId ??= Guid.NewGuid().ToString("N");
            var draft = new MailDraftRecord(
                _draftId,
                _account.Id,
                RecipientsBox.Text,
                SubjectBox.Text,
                BodyBox.Text,
                DateTimeOffset.UtcNow,
                _inReplyTo,
                _references is null ? null : string.Join(' ', _references));
            var attachments = _attachments
                .Select(item => new MailDraftAttachmentRecord(
                    item.FileName,
                    item.ContentType,
                    item.Content))
                .ToArray();

            await _database.SaveDraftAsync(draft, attachments);
            WasDraftSaved = true;
            _hasUnsavedDraftChanges = _draftRevision != revision;
        }
        finally
        {
            _draftSaveGate.Release();
        }
    }

    private void DeferCloseForDraftIfNeeded(System.ComponentModel.CancelEventArgs e)
    {
        if (!_hasUnsavedDraftChanges || _isCompleting)
        {
            return;
        }

        e.Cancel = true;
        if (_isSavingBeforeClose)
        {
            return;
        }

        _isSavingBeforeClose = true;
        _draftAutosaveTimer.Stop();
        _ = SaveDraftAndCloseAsync();
    }

    private async Task SaveDraftAndCloseAsync()
    {
        SetBusy(true);
        StatusText.Text = "Saving the latest changes before closing...";
        try
        {
            await PersistDraftAsync();
            if (_hasUnsavedDraftChanges)
            {
                throw new InvalidOperationException("The draft changed while it was being saved. Try closing again.");
            }

            SetBusy(false);
            _isCompleting = true;
            DialogResult = false;
        }
        catch (Exception exception)
        {
            SetBusy(false);
            _isSavingBeforeClose = false;
            StatusText.Text = $"The draft was not saved; this window remains open. {exception.Message}";
        }
    }
}
