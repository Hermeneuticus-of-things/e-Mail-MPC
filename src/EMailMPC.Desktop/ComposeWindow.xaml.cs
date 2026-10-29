using System.Windows;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class ComposeWindow : Window
{
    private readonly MailAccountRecord _account;
    private readonly WindowsUserSecretStore _secretStore;
    private bool _isBusy;
    private bool _isCompleting;

    public ComposeWindow(MailAccountRecord account, WindowsUserSecretStore secretStore)
    {
        InitializeComponent();
        _account = account;
        _secretStore = secretStore;
        FromText.Text = $"From: {account.DisplayName} <{account.EmailAddress}>";
    }

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
        CancelButton.IsEnabled = !busy;
    }
}
