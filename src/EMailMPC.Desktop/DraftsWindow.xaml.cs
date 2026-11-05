using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class DraftsWindow : Window
{
    private readonly EncryptedMailDatabase _database;
    private readonly MailAccountRecord _account;
    private readonly WindowFocusCoordinator _focusCoordinator;
    private bool _isLoading;

    public DraftsWindow(EncryptedMailDatabase database, MailAccountRecord account)
    {
        InitializeComponent();
        _database = database;
        _account = account;
        _focusCoordinator = WindowFocusCoordinator.Attach(this, "drafts");
        _focusCoordinator.RegisterSurface("drafts.surface", DraftsSurface, DraftsGrid);
        AccountText.Text = account.EmailAddress;
    }

    public MailDraftRecord? SelectedDraft { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDraftsAsync();
        _focusCoordinator.ActivateInitialSurface("drafts.surface");
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

        var choice = ShowMessageBox(
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
            var deletedIndex = DraftsGrid.SelectedIndex;
            await _database.DeleteDraftAsync(selected.Draft.Id);
            StatusText.Text = "Draft deleted.";
            await LoadDraftsAsync();
            if (DraftsGrid.Items.Count > 0)
            {
                DraftsGrid.SelectedIndex = Math.Min(deletedIndex, DraftsGrid.Items.Count - 1);
            }

            DraftsGrid.Focus();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Draft could not be deleted.";
            ShowMessageBox(
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
            ShowMessageBox(
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

    private MessageBoxResult ShowMessageBox(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.OK)
    {
        using (_focusCoordinator.PushModalSurface("dialog.drafts-message"))
        {
            return MessageBox.Show(owner, message, caption, buttons, image, defaultResult);
        }
    }

    private sealed record DraftListItem(MailDraftRecord Draft)
    {
        public string Subject => string.IsNullOrWhiteSpace(Draft.Subject) ? "(No subject)" : Draft.Subject;

        public string Recipients => Draft.Recipients;

        public string UpdatedText => Draft.UpdatedAtUtc.ToLocalTime().ToString("g");
    }
}
