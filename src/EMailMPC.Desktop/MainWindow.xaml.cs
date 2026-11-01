using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Search;
using EMailMPC.Core.Storage;
using EMailMPC.Core.Updates;
using Microsoft.Win32;

namespace EMailMPC.Desktop;

public partial class MainWindow : Window
{
    private bool _isLoadingAccounts;
    private bool _isLoadingFolders;
    private bool _isBusy;
    private int _accountCount;
    private long _previewRequestId;
    private MailMessageRecord? _selectedMessage;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || _isBusy)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;
            case Key.N:
                ComposeButton_Click(sender, e);
                e.Handled = true;
                break;
            case Key.R:
                SyncButton_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAccountsAsync();
        await RunSearchAsync();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BackupDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var application = (App)Application.Current;
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".empbackup",
            FileName = $"eMail-MPC-backup-{DateTime.Now:yyyyMMdd-HHmmss}.empbackup",
            Filter = "eMail MPC encrypted backup (*.empbackup)|*.empbackup",
            OverwritePrompt = true,
            Title = "Back up encrypted local mail data"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusy(true);
        StatusText.Text = "Creating an encrypted local backup...";
        try
        {
            await new LocalMailBackupService().CreateAsync(
                application.MailDatabase!,
                application.DatabaseKeyPath,
                application.AccountSecretsDirectory,
                dialog.FileName);
            StatusText.Text = "Encrypted backup created. It can be restored only where this Windows user can decrypt its DPAPI-protected key.";
            MessageBox.Show(
                this,
                "The encrypted backup was created. Keep it private and retain access to the same Windows user profile; the database key and account credentials are protected with Windows DPAPI and are not portable to another user or device.",
                "Backup complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Backup failed.";
            MessageBox.Show(
                this,
                $"The local backup could not be created.\n\n{exception.Message}",
                "Backup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "eMail MPC encrypted backup (*.empbackup)|*.empbackup",
            Multiselect = false,
            Title = "Choose an encrypted local backup"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Restoring replaces the current local mail database, protected account credentials, and drafts with the selected backup. The app will close afterward. Continue only if this is the backup you intend to restore.",
            "Replace local mail data?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var application = (App)Application.Current;
        SetBusy(true);
        StatusText.Text = "Validating and restoring the selected backup...";
        application.MailDatabase!.Dispose();
        try
        {
            var cleanupWarning = await new LocalMailBackupService().RestoreAsync(dialog.FileName, application.DataDirectory);
            MessageBox.Show(
                this,
                cleanupWarning is null
                    ? "The backup was restored. eMail MPC will close; start it again to open the restored mail data."
                    : $"The backup was restored, but the prior data copy could not be removed.\n\n{cleanupWarning}\n\neMail MPC will close; start it again to open the restored mail data.",
                "Restore complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Restore failed; the existing data was preserved when restoration had not committed.";
            MessageBox.Show(
                this,
                $"The backup could not be restored.\n\n{exception.Message}\n\nThe application will close. Start it again to reopen the current local data.",
                "Restore error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown(1);
        }
    }

    private void FocusSearchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private async void ClearSearchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        QueryPlanPanel.Visibility = Visibility.Collapsed;
        await RunSearchAsync();
    }

    private void FoldersMenuItem_Click(object sender, RoutedEventArgs e)
    {
        FolderColumn.Width = new GridLength(FoldersMenuItem.IsChecked ? 168 : 0);
    }

    private void ReaderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ReaderColumn.Width = ReaderMenuItem.IsChecked
            ? new GridLength(1.15, GridUnitType.Star)
            : new GridLength(0);
        ReaderSplitterColumn.Width = new GridLength(ReaderMenuItem.IsChecked ? 4 : 0);
    }

    private void SortNewestMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SortCombo.SelectedIndex = 0;
    }

    private void SortRelevanceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SortCombo.SelectedIndex = 1;
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "development";
        MessageBox.Show(
            this,
            $"eMail MPC {version}\n\nWindows desktop preview. Mail is stored in an encrypted local database. This build supports IMAP/SMTP with provider-issued app passwords; OAuth and cross-device data portability are not yet available.",
            "About eMail MPC",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void CheckForUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        StatusText.Text = "Checking the official release manifest...";
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var updateService = new VerifiedUpdateService(httpClient);
            var manifest = await updateService.GetManifestAsync();
            var currentVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            if (!VerifiedUpdateService.IsUpdateAvailable(currentVersion, manifest.Version))
            {
                StatusText.Text = $"eMail MPC {currentVersion} is up to date.";
                MessageBox.Show(
                    this,
                    $"This installation is version {currentVersion}. The latest stable release is {manifest.Version}.",
                    "No update available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var isPortable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"));
            var packageDescription = isPortable ? "portable ZIP" : "setup package";
            var packageSize = isPortable ? manifest.SizeBytes : manifest.InstallerSizeBytes;
            var downloadChoice = MessageBox.Show(
                this,
                $"Version {manifest.Version} is available. Download and verify the official {packageDescription} ({packageSize:N0} bytes)?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.No);
            if (downloadChoice != MessageBoxResult.Yes)
            {
                StatusText.Text = "Update download cancelled.";
                return;
            }

            var stagingDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eMail MPC",
                "updates");
            if (isPortable)
            {
                var archivePath = await updateService.DownloadAndVerifyPortableArchiveAsync(manifest, stagingDirectory);
                StatusText.Text = $"Version {manifest.Version} portable archive downloaded and verified.";
                MessageBox.Show(
                    this,
                    $"The portable archive passed its size and SHA-256 checks.\n\nSHA-256: {manifest.Sha256}\n\nTo update, close eMail MPC, keep a backup of the current application folder, and extract the archive over the existing program files. Preserve the existing data folder.",
                    "Verified portable update staged",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                StatusText.Text = $"Verified portable update is staged at {archivePath}.";
            }
            else
            {
                var installerPath = await updateService.DownloadAndVerifyInstallerAsync(manifest, stagingDirectory);
                StatusText.Text = $"Version {manifest.Version} setup package downloaded and verified.";
                var installChoice = MessageBox.Show(
                    this,
                    $"The setup package passed its size and SHA-256 checks.\n\nSHA-256: {manifest.InstallerSha256}\n\nRun the official setup wizard now? Your local mail data is stored separately from the program files.",
                    "Verified update ready",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (installChoice == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true
                    });
                    StatusText.Text = $"Starting the setup wizard for version {manifest.Version}.";
                }
                else
                {
                    StatusText.Text = $"Verified update is staged at {installerPath}.";
                }
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Update check or verification failed.";
            MessageBox.Show(
                this,
                $"An update could not be checked or verified. The setup package was not started.\n\n{exception.Message}",
                "Update check failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunSearchAsync();
    }

    private async void AddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var application = (App)Application.Current;
            StatusText.Text = "Opening account setup...";
            var dialog = new AccountSetupWindow(application.MailDatabase!, application.SecretStore!)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                await LoadAccountsAsync();
                StatusText.Text = "Account added. Mail is not synchronized until you choose Sync mail.";
            }
            else
            {
                StatusText.Text = "Account setup canceled.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Account setup could not be opened.";
            MessageBox.Show(
                this,
                $"The account setup window could not be opened.\n\n{exception.Message}",
                "Account setup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RemoveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountCombo.SelectedItem is not AccountFilterItem { Account: not null } selected)
        {
            StatusText.Text = "Choose one account to remove.";
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"Remove {selected.Account.EmailAddress} and its saved sign-in secret?\n\nDownloaded messages, attachments, and drafts stay on this device but are no longer linked to the account. No mail on the server is deleted.",
            "Remove email account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var application = (App)Application.Current;
        SetBusy(true);
        try
        {
            await application.MailDatabase!.DeleteAccountAsync(selected.Account.Id);
        }
        catch (Exception exception)
        {
            StatusText.Text = "The account could not be removed.";
            MessageBox.Show(
                this,
                $"The account could not be removed. Existing local mail was not intentionally deleted.\n\n{exception.Message}",
                "Account removal failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        Exception? secretCleanupError = null;
        try
        {
            application.SecretStore!.Delete(selected.Account.Id);
        }
        catch (Exception exception)
        {
            secretCleanupError = exception;
        }

        await LoadAccountsAsync();
        await RunSearchAsync();
        if (secretCleanupError is null)
        {
            StatusText.Text = $"Removed {selected.Account.EmailAddress}. Existing local mail was kept.";
            return;
        }

        StatusText.Text = "Account removed, but its saved sign-in secret could not be deleted.";
        MessageBox.Show(
            this,
            $"The account was removed and its local mail was kept, but the saved sign-in secret could not be deleted. Close the app and remove the secret file manually if the problem persists.\n\n{secretCleanupError.Message}",
            "Sign-in secret cleanup failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountCombo.SelectedItem is not AccountFilterItem { Account: not null } selected)
        {
            StatusText.Text = "Choose an account to synchronize.";
            return;
        }

        var application = (App)Application.Current;
        SetBusy(true);
        StatusText.Text = $"Connecting to {selected.Account.EmailAddress}...";
        var syncSucceeded = false;
        try
        {
            var password = application.SecretStore!.Load(selected.Account.Id);
            var folders = await application.MailDatabase!.ListFoldersAsync(selected.Account.Id);
            var service = new ImapMailService(application.MailDatabase);
            var result = await service.SynchronizeAsync(selected.Account, password, folders);
            StatusText.Text = $"Sync complete. {result.MessageCount} message(s) checked across {folders.Count} folder(s).";
            if (result.SkippedLargeBodyCount > 0)
            {
                StatusText.Text += $" {result.SkippedLargeBodyCount} body/bodies over 2 MiB were not indexed.";
            }

            syncSucceeded = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = "Mail synchronization failed.";
            MessageBox.Show(
                this,
                $"Mail could not be synchronized. No messages on the server were changed.\n\n{exception.Message}",
                "Synchronization failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }

        if (syncSucceeded)
        {
            await RunSearchAsync();
        }
    }

    private void ComposeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountCombo.SelectedItem is not AccountFilterItem { Account: not null } selected)
        {
            StatusText.Text = "Choose one account before composing a message.";
            return;
        }

        try
        {
            var application = (App)Application.Current;
            var dialog = new ComposeWindow(
                selected.Account,
                application.SecretStore!,
                application.MailDatabase!)
            {
                Owner = this
            };
            _ = dialog.ShowDialog();
            if (dialog.WasSent)
            {
                StatusText.Text = $"Message accepted by SMTP for {selected.Account.EmailAddress}. Check Sent for the provider's saved copy.";
            }
            else if (dialog.WasDraftSaved)
            {
                StatusText.Text = "Draft saved locally in the encrypted store.";
            }
            else
            {
                StatusText.Text = "Message composition canceled.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "The compose window could not be opened.";
            MessageBox.Show(
                this,
                $"The compose window could not be opened.\n\n{exception.Message}",
                "Compose error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DraftsButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountCombo.SelectedItem is not AccountFilterItem { Account: not null } selected)
        {
            StatusText.Text = "Choose one account to view its saved drafts.";
            return;
        }

        var application = (App)Application.Current;
        var draftsWindow = new DraftsWindow(application.MailDatabase!, selected.Account)
        {
            Owner = this
        };
        if (draftsWindow.ShowDialog() != true || draftsWindow.SelectedDraft is not { } draft)
        {
            return;
        }

        try
        {
            var composeWindow = new ComposeWindow(
                selected.Account,
                application.SecretStore!,
                application.MailDatabase!,
                draft)
            {
                Owner = this
            };
            _ = composeWindow.ShowDialog();
            StatusText.Text = composeWindow.WasSent
                ? $"Message accepted by SMTP for {selected.Account.EmailAddress}. Check Sent for the provider's saved copy."
                : composeWindow.WasDraftSaved
                    ? "Draft saved locally in the encrypted store."
                    : "Draft editing canceled.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Draft could not be opened.";
            MessageBox.Show(
                this,
                $"The selected draft could not be opened.\n\n{exception.Message}",
                "Draft error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var requestId = ++_previewRequestId;
        if (ResultsGrid.SelectedItem is not MailSearchResult selected)
        {
            _selectedMessage = null;
            MessageSubjectText.Text = "Select a message to read";
            MessageMetadataText.Text = string.Empty;
            MessageBodyText.Clear();
            AttachmentsGrid.ItemsSource = null;
            return;
        }

        _selectedMessage = null;
        MessageSubjectText.Text = selected.Subject;
        MessageMetadataText.Text = $"Loading message from {selected.Sender}...";
        MessageBodyText.Clear();
        AttachmentsGrid.ItemsSource = null;
        try
        {
            var application = (App)Application.Current;
            var message = await application.MailDatabase!.GetMessageAsync(selected.Id);
            if (requestId != _previewRequestId)
            {
                return;
            }

            if (message is null)
            {
                MessageMetadataText.Text = "This message is no longer available in the local store.";
                return;
            }

            MessageSubjectText.Text = message.Subject;
            MessageMetadataText.Text =
                $"From: {message.Sender}  ·  To: {message.Recipients}  ·  {message.ReceivedAtUtc.ToLocalTime():g}  ·  {message.Folder}";
            MessageBodyText.Text = message.BodyText;
            _selectedMessage = message;
            await RefreshAttachmentItemsAsync(message.Id, requestId);
        }
        catch (Exception exception)
        {
            if (requestId != _previewRequestId)
            {
                return;
            }

            MessageMetadataText.Text = "Could not load this message.";
            MessageBox.Show(
                this,
                $"The selected local message could not be loaded.\n\n{exception.Message}",
                "Message read failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SaveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AttachmentListItem item } ||
            _selectedMessage is not { } message ||
            !item.CanSave)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = false,
            CheckPathExists = true,
            FileName = GetSafeFileName(item.Attachment.FileName),
            OverwritePrompt = true,
            Title = "Save email attachment"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var application = (App)Application.Current;
        byte[]? content = null;
        SetBusy(true);
        StatusText.Text = $"Saving {item.Attachment.FileName}...";
        try
        {
            content = await application.MailDatabase!.GetAttachmentContentAsync(item.Attachment.Id);
            if (content is null)
            {
                if (message.AccountId is null)
                {
                    throw new InvalidOperationException("The original email account is no longer configured, so this attachment is not available locally.");
                }

                var accounts = await application.MailDatabase.ListAccountsAsync();
                var account = accounts.SingleOrDefault(candidate => candidate.Id == message.AccountId)
                    ?? throw new InvalidOperationException("The original email account is no longer configured.");
                var folders = await application.MailDatabase.ListFoldersAsync(account.Id);
                var folder = folders.SingleOrDefault(candidate =>
                    string.Equals(candidate.FullName, message.Folder, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("The original email folder is no longer configured.");
                var uidSeparator = message.Id.LastIndexOf(':');
                if (uidSeparator < 0 ||
                    !uint.TryParse(message.Id[(uidSeparator + 1)..], out var uid))
                {
                    throw new InvalidDataException("The local message does not contain a valid IMAP UID.");
                }

                var password = application.SecretStore!.Load(account.Id);
                content = await new ImapMailService(application.MailDatabase).DownloadAttachmentAsync(
                    account,
                    password,
                    folder,
                    uid,
                    item.Attachment);
            }

            if (content.Length > EncryptedMailDatabase.MaximumAttachmentBytes)
            {
                throw new InvalidDataException(
                    $"The attachment exceeds the {EncryptedMailDatabase.MaximumAttachmentBytes / (1024 * 1024)} MiB save limit.");
            }

            await SaveFileAtomicallyAsync(dialog.FileName, content);
            StatusText.Text = $"Saved attachment to {dialog.FileName}.";
            await RefreshAttachmentItemsAsync(message.Id, _previewRequestId);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Attachment could not be saved.";
            MessageBox.Show(
                this,
                $"The attachment could not be saved. It was not opened automatically.\n\n{exception.Message}",
                "Attachment save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (content is not null)
            {
                CryptographicOperations.ZeroMemory(content);
            }

            SetBusy(false);
        }
    }

    private async Task RefreshAttachmentItemsAsync(string messageId, long requestId)
    {
        var application = (App)Application.Current;
        var attachments = await application.MailDatabase!.ListMessageAttachmentsAsync(messageId);
        if (requestId == _previewRequestId)
        {
            AttachmentsGrid.ItemsSource = attachments
                .Select(attachment => new AttachmentListItem(attachment))
                .ToArray();
        }
    }

    private static async Task SaveFileAtomicallyAsync(string filePath, byte[] content)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The selected save path has no parent directory.");
        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetSafeFileName(string fileName)
    {
        var leafName = fileName.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(leafName
            .Where(character => !char.IsControl(character) && !invalidCharacters.Contains(character))
            .Take(240)
            .ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safeName) ? "attachment" : safeName;
    }

    private async void AccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingAccounts || !IsLoaded)
        {
            return;
        }

        UpdateAccountActions();
        await LoadFoldersAsync();
        await RunSearchAsync();
    }

    private async void FolderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingFolders || _isLoadingAccounts || !IsLoaded)
        {
            return;
        }

        await RunSearchAsync();
    }

    private async void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await RunSearchAsync();
    }

    private async void QueryPlanBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SearchBox.Text = QueryPlanBox.Text;
            await RunSearchAsync();
        }
    }

    private async Task LoadAccountsAsync()
    {
        _isLoadingAccounts = true;
        try
        {
            var application = (App)Application.Current;
            var accounts = await application.MailDatabase!.ListAccountsAsync();
            _accountCount = accounts.Count;
            var items = new List<AccountFilterItem> { new("All accounts", null) };
            items.AddRange(accounts.Select(account => new AccountFilterItem(account.EmailAddress, account)));
            AccountCombo.ItemsSource = items;
            AccountCombo.SelectedIndex = 0;
            AccountCountText.Text = accounts.Count == 0
                ? "No accounts connected"
                : $"{accounts.Count} account(s) · local only";
            MenuRemoveAccountItem.IsEnabled = accounts.Count > 0 && !_isBusy;
            UpdateAccountActions();
        }
        catch (Exception exception)
        {
            AccountCountText.Text = "Could not load accounts";
            MessageBox.Show(
                this,
                $"Saved accounts could not be loaded.\n\n{exception.Message}",
                "Account loading failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingAccounts = false;
        }

        await LoadFoldersAsync();
    }

    private async Task LoadFoldersAsync()
    {
        _isLoadingFolders = true;
        try
        {
            var items = new List<FolderFilterItem> { new("All mail", null) };
            if (AccountCombo.SelectedItem is AccountFilterItem { Account: not null } selected)
            {
                var application = (App)Application.Current;
                var folders = await application.MailDatabase!.ListFoldersAsync(selected.Account.Id);
                items.AddRange(folders.Select(folder => new FolderFilterItem(folder.FullName, folder)));
            }

            FolderListBox.ItemsSource = items;
            FolderListBox.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            FolderListBox.ItemsSource = new[] { new FolderFilterItem("All mail", null) };
            FolderListBox.SelectedIndex = 0;
            StatusText.Text = "Mail folders could not be loaded.";
            MessageBox.Show(
                this,
                $"Saved mail folders could not be loaded.\n\n{exception.Message}",
                "Folder loading failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingFolders = false;
        }
    }

    private async Task RunSearchAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        StatusText.Text = "Searching the encrypted local store...";

        try
        {
            var application = (App)Application.Current;
            var accountId = (AccountCombo.SelectedItem as AccountFilterItem)?.Account?.Id;
            var plan = NaturalLanguageQueryInterpreter.Interpret(SearchBox.Text);
            var effectiveText = SearchBox.Text;
            if (plan.IsNaturalLanguage)
            {
                effectiveText = plan.QuerySyntax;
                QueryPlanBox.Text = plan.QuerySyntax;
                QueryPlanPanel.Visibility = Visibility.Visible;
                StatusText.Text = "Natural-language query interpreted — edit the plan below and press Enter to refine.";
            }
            else
            {
                QueryPlanPanel.Visibility = Visibility.Collapsed;
            }

            var parsedQuery = MailQueryParser.Parse(effectiveText, accountId);
            var folderFilter = (FolderListBox.SelectedItem as FolderFilterItem)?.Folder?.FullName;
            if (parsedQuery.Criteria.Folder is null && folderFilter is not null)
            {
                parsedQuery = parsedQuery with
                {
                    Criteria = CopyWithFolder(parsedQuery.Criteria, folderFilter)
                };
            }

            var results = await application.MailDatabase!.SearchAsync(
                parsedQuery,
                sort: SortCombo.SelectedIndex == 1 ? SearchSort.Relevance : SearchSort.DateNewestFirst);
            ResultsGrid.ItemsSource = results;
            EmptyResultsText.Text = _accountCount == 0
                ? "Add an email account to sync and search your mail."
                : "No messages match this search.";
            EmptyResultsText.Visibility = results.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (!plan.IsNaturalLanguage)
            {
                StatusText.Text = results.Count == 0
                    ? "No matching local messages."
                    : $"{results.Count} local message(s) found.";
            }
            else if (results.Count == 0)
            {
                StatusText.Text = "Interpreted query matched no local messages.";
            }
            else
            {
                StatusText.Text = $"Interpreted query found {results.Count} local message(s).";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Search failed.";
            MessageBox.Show(
                this,
                $"Local mail search failed.\n\n{exception.Message}",
                "Search error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        SearchBox.IsEnabled = !isBusy;
        SearchButton.IsEnabled = !isBusy;
        MenuAddAccountItem.IsEnabled = !isBusy;
        BackupDataMenuItem.IsEnabled = !isBusy;
        RestoreDataMenuItem.IsEnabled = !isBusy;
        CheckForUpdatesMenuItem.IsEnabled = !isBusy;
        AccountCombo.IsEnabled = !isBusy;
        FolderListBox.IsEnabled = !isBusy;
        AttachmentsGrid.IsEnabled = !isBusy;
        UpdateAccountActions();
    }

    private void UpdateAccountActions()
    {
        var hasSelectedAccount = (AccountCombo.SelectedItem as AccountFilterItem)?.Account is not null;
        MenuAddAccountItem.IsEnabled = !_isBusy;
        MenuSyncItem.IsEnabled = !_isBusy && hasSelectedAccount;
        MenuDraftsItem.IsEnabled = !_isBusy && hasSelectedAccount;
        MenuRemoveAccountItem.IsEnabled = !_isBusy && hasSelectedAccount;
    }

    private static SearchCriteria CopyWithFolder(SearchCriteria criteria, string folder)
    {
        return new SearchCriteria(
            text: criteria.Text,
            sender: criteria.Sender,
            recipient: criteria.Recipient,
            folder: folder,
            after: criteria.After,
            before: criteria.Before,
            isRead: criteria.IsRead,
            isFlagged: criteria.IsFlagged,
            hasAttachments: criteria.HasAttachments,
            accountId: criteria.AccountId);
    }

    private sealed record AccountFilterItem(string Label, MailAccountRecord? Account);

    private sealed record FolderFilterItem(string Label, MailFolderRecord? Folder);

    private sealed record AttachmentListItem(MailAttachmentRecord Attachment)
    {
        public string FileName => Attachment.FileName;

        public string SizeText => Attachment.SizeBytes < 1024 * 1024
            ? $"{Attachment.SizeBytes / 1024.0:N0} KB"
            : $"{Attachment.SizeBytes / (1024.0 * 1024):N1} MB";

        public string StatusText => Attachment.ContentState == AttachmentContentState.Downloaded
            ? "Downloaded locally"
            : "On server";

        public string ActionText => Attachment.ContentState == AttachmentContentState.Downloaded
            ? "Save copy"
            : "Download";

        public bool CanSave => Attachment.SizeBytes <= EncryptedMailDatabase.MaximumAttachmentBytes;
    }
}