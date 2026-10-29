using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EMailMPC.Core.Accounts;
using EMailMPC.Core.Search;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class MainWindow : Window
{
    private bool _isLoadingAccounts;
    private bool _isBusy;
    private long _previewRequestId;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAccountsAsync();
        await RunSearchAsync();
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

    private async void ComposeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountCombo.SelectedItem is not AccountFilterItem { Account: not null } selected)
        {
            StatusText.Text = "Choose one account before composing a message.";
            return;
        }

        try
        {
            var application = (App)Application.Current;
            var dialog = new ComposeWindow(selected.Account, application.SecretStore!)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = $"Message accepted by SMTP for {selected.Account.EmailAddress}. Check Sent for the provider's saved copy.";
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

    private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var requestId = ++_previewRequestId;
        if (ResultsGrid.SelectedItem is not MailSearchResult selected)
        {
            MessageSubjectText.Text = "Select a message to read";
            MessageMetadataText.Text = string.Empty;
            MessageBodyText.Clear();
            return;
        }

        MessageSubjectText.Text = selected.Subject;
        MessageMetadataText.Text = $"Loading message from {selected.Sender}...";
        MessageBodyText.Clear();
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

    private async void AccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingAccounts || !IsLoaded)
        {
            return;
        }

        UpdateAccountActions();
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
            var items = new List<AccountFilterItem> { new("All accounts", null) };
            items.AddRange(accounts.Select(account => new AccountFilterItem(account.EmailAddress, account)));
            AccountCombo.ItemsSource = items;
            AccountCombo.SelectedIndex = 0;
            AccountCountText.Text = accounts.Count == 0
                ? "No accounts connected"
                : $"{accounts.Count} account(s) · local only";
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

            var results = await application.MailDatabase!.SearchAsync(
                MailQueryParser.Parse(effectiveText, accountId),
                sort: SortCombo.SelectedIndex == 1 ? SearchSort.Relevance : SearchSort.DateNewestFirst);
            ResultsGrid.ItemsSource = results;
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
        AddAccountButton.IsEnabled = !isBusy;
        AccountCombo.IsEnabled = !isBusy;
        UpdateAccountActions();
    }

    private void UpdateAccountActions()
    {
        var hasSelectedAccount = (AccountCombo.SelectedItem as AccountFilterItem)?.Account is not null;
        SyncButton.IsEnabled = !_isBusy && hasSelectedAccount;
        ComposeButton.IsEnabled = !_isBusy && hasSelectedAccount;
    }

    private sealed record AccountFilterItem(string Label, MailAccountRecord? Account);
}