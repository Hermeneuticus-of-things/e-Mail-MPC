using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class DraftsWindow : Window
{
    private readonly EncryptedMailDatabase _database;
    private readonly MailAccountRecord _account;
    private bool _isLoading;

    public DraftsWindow(EncryptedMailDatabase database, MailAccountRecord account)
    {
        InitializeComponent();
        _database = database;
        _account = account;
        AccountText.Text = account.EmailAddress;
    }

    public MailDraftRecord? SelectedDraft { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDraftsAsync();
    }

    private void DraftsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();
    }

    private void UpdateSelectionActions()
    {
        var hasSelection = DraftsGrid.SelectedItem is DraftListItem;
        OpenButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection && !_isLoading;
    }

    private void DraftsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DraftsGrid.SelectedItem is DraftListItem)
        {
            OpenSelectedDraft();
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedDraft();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DraftsGrid.SelectedItem is not DraftListItem selected)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"Permanently delete the saved draft “{selected.Draft.Subject}”?",
            "Delete saved draft",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        _isLoading = true;
        DeleteButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        try
        {
            await _database.DeleteDraftAsync(selected.Draft.Id);
            StatusText.Text = "Draft deleted.";
            await LoadDraftsAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Draft could not be deleted.";
            MessageBox.Show(
                this,
                $"The selected draft could not be deleted.\n\n{exception.Message}",
                "Draft deletion failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
            UpdateSelectionActions();
        }
    }

    private async Task LoadDraftsAsync()
    {
        _isLoading = true;
        OpenButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        StatusText.Text = "Loading drafts...";
        try
        {
            var drafts = await _database.ListDraftsAsync(_account.Id);
            DraftsGrid.ItemsSource = drafts.Select(draft => new DraftListItem(draft)).ToArray();
            StatusText.Text = drafts.Count == 0
                ? "No saved drafts for this account."
                : $"{drafts.Count} saved draft(s).";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Drafts could not be loaded.";
            MessageBox.Show(
                this,
                $"Saved drafts could not be loaded.\n\n{exception.Message}",
                "Draft loading failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
            UpdateSelectionActions();
        }
    }

    private void OpenSelectedDraft()
    {
        if (DraftsGrid.SelectedItem is not DraftListItem selected)
        {
            return;
        }

        SelectedDraft = selected.Draft;
        DialogResult = true;
    }

    private sealed record DraftListItem(MailDraftRecord Draft)
    {
        public string Subject => string.IsNullOrWhiteSpace(Draft.Subject) ? "(No subject)" : Draft.Subject;

        public string Recipients => Draft.Recipients;

        public string UpdatedText => Draft.UpdatedAtUtc.ToLocalTime().ToString("g");
    }
}
