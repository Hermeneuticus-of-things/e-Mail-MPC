using System.Windows;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class ComposeWindow : Window
{
    private readonly MailAccountRecord _account;
    private readonly WindowsUserSecretStore _secretStore;
    private readonly EncryptedMailDatabase _database;
    private string? _draftId;
    private bool _isBusy;
    private bool _isCompleting;

    public ComposeWindow(
        MailAccountRecord account,
        WindowsUserSecretStore secretStore,
        EncryptedMailDatabase database,
        MailDraftRecord? draft = null)
    {
        InitializeComponent();
        _account = account;
        _secretStore = secretStore;
        _database = database;
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
        }
    }

    public bool WasSent { get; private set; }

    public bool WasDraftSaved { get; private set; }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        StatusText.Text = "Connecting to the configured SMTP server...";
        try
        {
            var password = _secretStore.Load(_account.Id);
            await new SmtpMailService().SendAsync(
                _account,
                password,
                RecipientsBox.Text,
                SubjectBox.Text,
                BodyBox.Text);
            StatusText.Text = "Message accepted by the SMTP server.";
            WasSent = true;
            if (_draftId is not null)
            {
                try
                {
                    await _database.DeleteDraftAsync(_draftId);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        $"The message was sent, but its saved draft could not be removed. Delete the draft before sending it again.\n\n{exception.Message}",
                        "Draft cleanup failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            BodyBox.Clear();
            _isCompleting = true;
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            StatusText.Text = "Message was not sent.";
            MessageBox.Show(
                this,
                $"Check the message fields. The SMTP server was not contacted.\n\n{exception.Message}",
                "Check message details",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Message was not confirmed as sent.";
            MessageBox.Show(
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

    private async void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        StatusText.Text = "Saving draft in the encrypted local store...";
        try
        {
            _draftId ??= Guid.NewGuid().ToString("N");
            await _database.SaveDraftAsync(new MailDraftRecord(
                _draftId,
                _account.Id,
                RecipientsBox.Text,
                SubjectBox.Text,
                BodyBox.Text,
                DateTimeOffset.UtcNow));
            WasDraftSaved = true;
            StatusText.Text = "Draft saved locally.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Draft could not be saved.";
            MessageBox.Show(
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
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RecipientsBox.IsEnabled = !busy;
        SubjectBox.IsEnabled = !busy;
        BodyBox.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        SaveDraftButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
    }
}
