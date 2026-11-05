using System.Windows;
using System.Collections.ObjectModel;
using System.IO;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;
using Microsoft.Win32;

namespace EMailMPC.Desktop;

public partial class ComposeWindow : Window
{
    private readonly MailAccountRecord _account;
    private readonly WindowsUserSecretStore _secretStore;
    private readonly EncryptedMailDatabase _database;
    private readonly WindowFocusCoordinator _focusCoordinator;
    private readonly ObservableCollection<OutgoingAttachmentItem> _attachments = [];
    private string? _draftId;
    private bool _isBusy;
    private bool _isCompleting;

    public ComposeWindow(
        MailAccountRecord account,
        WindowsUserSecretStore secretStore,
        EncryptedMailDatabase database,
        MailDraftRecord? draft = null,
        MessageResponseDraft? response = null,
        IReadOnlyCollection<MailDraftAttachmentRecord>? draftAttachments = null)
    {
        if (draft is not null && response is not null)
        {
            throw new ArgumentException("A compose window cannot be initialized with both a saved draft and a message response.");
        }

        InitializeComponent();
        InitializeDraftAutosave();
        _account = account;
        _secretStore = secretStore;
        _database = database;
        _focusCoordinator = WindowFocusCoordinator.Attach(this, "compose");
        _focusCoordinator.RegisterSurface("compose.message", ComposeSurface, BodyBox);
        AttachmentsList.ItemsSource = _attachments;
        FromText.Text = $"From: {account.DisplayName} <{account.EmailAddress}>";
        if (draft is not null)
        {
            if (!string.Equals(draft.AccountId, account.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException("The draft belongs to a different email account.", nameof(draft));
            }

            _draftId = draft.Id;
            RecipientsBox.Text = draft.Recipients;
            SubjectBox.Text = draft.Subject;
            BodyBox.Text = draft.BodyText;
            RestoreThreadingHeaders(draft);
            foreach (var attachment in draftAttachments ?? [])
            {
                _attachments.Add(new OutgoingAttachmentItem(
                    null,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.Content));
            }
        }
        else if (response is not null)
        {
            RecipientsBox.Text = response.Recipients;
            SubjectBox.Text = response.Subject;
            BodyBox.Text = response.Body;
            RestoreThreadingHeaders(response);
        }
        CompleteDraftInitialization(response);
    }

    public bool WasSent { get; private set; }

    public bool WasDraftSaved { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _focusCoordinator.ActivateInitialSurface("compose.message");
        BodyBox.CaretIndex = BodyBox.Text.Length;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        _draftAutosaveTimer.Stop();
        StatusText.Text = "Connecting to the configured SMTP server...";
        try
        {
            if (_hasUnsavedDraftChanges)
            {
                try
                {
                    await PersistDraftAsync();
                }
                catch (Exception exception)
                {
                    StatusText.Text = "Draft could not be saved; SMTP was not contacted.";
                    ShowMessageBox(
                        this,
                        $"The latest changes could not be saved as a draft, so the message was not sent.\n\n{exception.Message}",
                        "Draft save failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            var password = _secretStore.Load(_account.Id);
            var sentMessageId = await new SmtpMailService().SendWithMessageIdAsync(
                _account,
                password,
                new SmtpMailService.OutgoingMessage(
                    RecipientsBox.Text,
                    SubjectBox.Text,
                    BodyBox.Text,
                    _attachments.Select(item => item.Attachment).ToArray(),
                    InReplyTo: _inReplyTo,
                    References: _references));
            StatusText.Text = "Message accepted by the SMTP server.";
            WasSent = true;

            var localCopySaved = false;
            try
            {
                var sentAt = DateTimeOffset.UtcNow;
                var localSentId = $"sent-local:{_account.Id}:{Guid.NewGuid():N}";
                await _database.SaveMessageAsync(new MailMessageRecord(
                    localSentId,
                    _account.EmailAddress,
                    RecipientsBox.Text,
                    SubjectBox.Text,
                    BodyBox.Text,
                    sentAt,
                    "Sent (local)",
                    true,
                    false,
                    _attachments.Count > 0,
                    _account.Id,
                    sentMessageId,
                    _inReplyTo,
                    _references is null ? null : string.Join(' ', _references)));

                if (_attachments.Count > 0)
                {
                    var attachmentRecords = _attachments
                        .Select((item, index) => new MailAttachmentRecord(
                            $"{localSentId}:attachment:{index}",
                            localSentId,
                            (index + 1).ToString(),
                            item.FileName,
                            item.ContentType,
                            item.Content.Length,
                            null,
                            false,
                            AttachmentContentState.NotDownloaded))
                        .ToArray();
                    await _database.ReplaceMessageAttachmentsAsync(localSentId, attachmentRecords);
                    for (var index = 0; index < _attachments.Count; index++)
                    {
                        await _database.SetAttachmentContentAsync(
                            attachmentRecords[index].Id,
                            _attachments[index].Content);
                    }
                }

                localCopySaved = true;
            }
            catch (Exception exception)
            {
                ShowMessageBox(
                    this,
                    $"The SMTP server accepted the message, but eMail MPC could not save its local Sent copy. Check the provider's Sent folder before retrying. Any saved draft has been kept.\n\n{exception.Message}",
                    "Local Sent copy could not be saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (_draftId is not null && localCopySaved)
            {
                try
                {
                    await _database.DeleteDraftAsync(_draftId);
                }
                catch (Exception exception)
                {
                    ShowMessageBox(
                        this,
                        $"The message was sent, but its saved draft could not be removed. Delete the draft before sending it again.\n\n{exception.Message}",
                        "Draft cleanup failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else if (_draftId is not null)
            {
                StatusText.Text = "Message accepted. The saved draft was kept because the local Sent copy could not be saved.";
            }

            _isCompleting = true;
            _draftAutosaveTimer.Stop();
            BodyBox.Clear();
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            StatusText.Text = "Message was not sent.";
            ShowMessageBox(
                this,
                $"Check the message fields. The SMTP server was not contacted.\n\n{exception.Message}",
                "Check message details",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Message was not confirmed as sent.";
            ShowMessageBox(
                this,
                $"The send operation did not complete successfully. The SMTP server may have accepted the message before the connection failed; check the provider's Sent folder before retrying to avoid duplicates.\n\n{exception.Message}",
                "Send status uncertain",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AttachFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = true,
            Title = "Choose files to attach"
        };
        using (_focusCoordinator.PushModalSurface("dialog.compose-attachments"))
        {
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
        }

        var selectedPaths = dialog.FileNames
            .Where(path => !_attachments.Any(item =>
                string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (selectedPaths.Length == 0)
        {
            StatusText.Text = "No new files selected.";
            return;
        }

        SetBusy(true);
        StatusText.Text = "Reading and checking selected files...";
        try
        {
            var selectedSizes = selectedPaths.Select(path => new FileInfo(path).Length).ToArray();
            if (selectedSizes.Any(size => size > EncryptedMailDatabase.MaximumAttachmentBytes))
            {
                throw new InvalidDataException(
                    $"Each attachment must be no larger than {EncryptedMailDatabase.MaximumAttachmentBytes / (1024 * 1024)} MiB.");
            }

            var existingBytes = _attachments.Sum(item => (long)item.Attachment.Content.Length);
            if (selectedSizes.Sum() + existingBytes > EncryptedMailDatabase.MaximumAttachmentBytes)
            {
                throw new InvalidDataException(
                    $"The combined attachment size must not exceed {EncryptedMailDatabase.MaximumAttachmentBytes / (1024 * 1024)} MiB.");
            }

            var loadedAttachments = new List<OutgoingAttachmentItem>(selectedPaths.Length);
            foreach (var path in selectedPaths)
            {
                var remainingBytes = EncryptedMailDatabase.MaximumAttachmentBytes -
                    _attachments.Sum(item => (long)item.Attachment.Content.Length);
                var content = await ReadAttachmentAsync(path, remainingBytes);
                loadedAttachments.Add(new OutgoingAttachmentItem(
                    path,
                    Path.GetFileName(path),
                    "application/octet-stream",
                    content));
            }

            foreach (var attachment in loadedAttachments)
            {
                _attachments.Add(attachment);
            }

            MarkDraftDirty();
            StatusText.Text = $"{_attachments.Count} file(s) attached ({_attachments.Sum(item => (long)item.Attachment.Content.Length) / (1024.0 * 1024):N1} MiB total).";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = "One or more files could not be attached.";
            ShowMessageBox(
                this,
                $"The files could not be attached. Choose readable files within the per-message size limit.\n\n{exception.Message}",
                "Attachment error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static async Task<byte[]> ReadAttachmentAsync(string path, long maximumBytes)
    {
        await using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length > maximumBytes)
        {
            throw new InvalidDataException("The selected files exceed the combined attachment size limit.");
        }

        using var content = new MemoryStream((int)Math.Min(source.Length, maximumBytes));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                return content.ToArray();
            }

            if (content.Length + read > maximumBytes)
            {
                throw new InvalidDataException("A selected file grew beyond the combined attachment size limit while being read.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (AttachmentsList.SelectedItem is OutgoingAttachmentItem selected)
        {
            _attachments.Remove(selected);
            MarkDraftDirty();
            StatusText.Text = _attachments.Count == 0
                ? "Selected attachment removed."
                : $"{_attachments.Count} file(s) attached ({_attachments.Sum(item => (long)item.Attachment.Content.Length) / (1024.0 * 1024):N1} MiB total).";
        }
    }

    private void AttachmentsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RemoveAttachmentButton.IsEnabled = AttachmentsList.SelectedItem is OutgoingAttachmentItem;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isBusy && !_isCompleting)
        {
            e.Cancel = true;
            StatusText.Text = "Wait for the SMTP operation to finish before closing. A canceled send may already have reached the server.";
            return;
        }

        DeferCloseForDraftIfNeeded(e);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RecipientsBox.IsEnabled = !busy;
        SubjectBox.IsEnabled = !busy;
        BodyBox.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        SaveDraftButton.IsEnabled = !busy;
        AttachFilesButton.IsEnabled = !busy;
        RemoveAttachmentButton.IsEnabled = !busy && AttachmentsList.SelectedItem is OutgoingAttachmentItem;
        CancelButton.IsEnabled = !busy;
    }

    private sealed record OutgoingAttachmentItem(
        string? Path,
        string FileName,
        string ContentType,
        byte[] Content)
    {
        public SmtpMailService.OutgoingAttachment Attachment => new(FileName, Content);

        public string DisplayText =>
            $"{FileName} — {Content.Length / 1024.0:N0} KB";
    }

    private MessageBoxResult ShowMessageBox(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.OK)
    {
        using (_focusCoordinator.PushModalSurface("dialog.compose-message"))
        {
            return MessageBox.Show(owner, message, caption, buttons, image, defaultResult);
        }
    }
}
