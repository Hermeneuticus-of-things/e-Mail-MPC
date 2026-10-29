using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class AccountSetupWindow : Window
{
    private readonly EncryptedMailDatabase _database;
    private readonly WindowsUserSecretStore _secretStore;
    private CancellationTokenSource? _operationCancellation;
    private bool _isBusy;
    private bool _closeWhenCancelled;
    private bool _isCompleting;

    public AccountSetupWindow(EncryptedMailDatabase database, WindowsUserSecretStore secretStore)
    {
        InitializeComponent();
        _database = database;
        _secretStore = secretStore;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        MailAccountRecord account;
        try
        {
            account = CreateAccount();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            ShowInputError(exception.Message);
            return;
        }

        var password = PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowInputError("Enter the app password issued by your email provider.");
            return;
        }

        SetBusy(true);
        _operationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellationToken = _operationCancellation.Token;
        StatusText.Text = "Verifying encrypted TLS connection and discovering folders...";
        try
        {
            var existingAccounts = await _database.ListAccountsAsync(cancellationToken);
            if (existingAccounts.Any(existing =>
                    string.Equals(existing.EmailAddress, account.EmailAddress, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("That email address is already configured.");
            }

            var folders = await new ImapMailService(_database).TestAndListFoldersAsync(account, password, cancellationToken);
            _secretStore.Save(account.Id, password);
            try
            {
                await _database.SaveAccountAsync(account, folders, cancellationToken);
            }
            catch
            {
                _secretStore.Delete(account.Id);
                throw;
            }

            StatusText.Text = $"Connected. Found {folders.Count} folder(s).";
            PasswordBox.Clear();
            _isCompleting = true;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText.Text = "Account setup canceled.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Account was not added.";
            MessageBox.Show(
                this,
                $"The IMAP account could not be verified and saved.\n\n{exception.Message}",
                "Account setup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PasswordBox.Clear();
            SetBusy(false);
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            if (_closeWhenCancelled)
            {
                _closeWhenCancelled = false;
                Close();
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        PasswordBox.Clear();
        DialogResult = false;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isBusy || _isCompleting)
        {
            return;
        }

        e.Cancel = true;
        _closeWhenCancelled = true;
        StatusText.Text = "Cancelling account setup...";
        _operationCancellation?.Cancel();
    }

    private MailAccountRecord CreateAccount()
    {
        var emailAddress = EmailBox.Text.Trim();
        if (emailAddress.Length > 320 ||
            !string.Equals(
                new MailAddress(emailAddress).Address,
                emailAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Enter a valid email address.");
        }

        var id = Guid.NewGuid().ToString("N");
        return new MailAccountRecord(
            id,
            emailAddress,
            DisplayNameBox.Text.Trim(),
            ImapHostBox.Text.Trim(),
            int.Parse(ImapPortBox.Text.Trim(), System.Globalization.CultureInfo.InvariantCulture),
            GetSecuritySelection(ImapSecurityBox),
            SmtpHostBox.Text.Trim(),
            int.Parse(SmtpPortBox.Text.Trim(), System.Globalization.CultureInfo.InvariantCulture),
            GetSecuritySelection(SmtpSecurityBox),
            MailAuthenticationMethod.AppPassword);
    }

    private static MailTransportSecurity GetSecuritySelection(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem { Tag: string value } ||
            !Enum.TryParse<MailTransportSecurity>(value, out var security))
        {
            throw new ArgumentException("Select a supported TLS security mode.");
        }

        return security;
    }

    private void ShowInputError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(
            this,
            message,
            "Check account details",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        ConnectButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        EmailBox.IsEnabled = !busy;
        DisplayNameBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        ImapHostBox.IsEnabled = !busy;
        ImapPortBox.IsEnabled = !busy;
        ImapSecurityBox.IsEnabled = !busy;
        SmtpHostBox.IsEnabled = !busy;
        SmtpPortBox.IsEnabled = !busy;
        SmtpSecurityBox.IsEnabled = !busy;
    }
}
