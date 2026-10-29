using System.IO;
using System.Security.Cryptography;
using System.Windows;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public EncryptedMailDatabase? MailDatabase { get; private set; }
    public WindowsUserSecretStore? SecretStore { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eMail MPC",
                "data");
            var keyPath = Path.Combine(dataDirectory, "keys", "database.dpapi");
            var databasePath = Path.Combine(dataDirectory, "mail.db");
            var secretsDirectory = Path.Combine(dataDirectory, "keys", "accounts");
            if (File.Exists(databasePath) && !File.Exists(keyPath))
            {
                throw new CryptographicException(
                    "The encrypted mail database exists but its protected key is missing. A replacement key was not created.");
            }

            var key = new WindowsUserKeyStore().LoadOrCreate(keyPath);

            try
            {
                MailDatabase = new EncryptedMailDatabase(databasePath, key);
                MailDatabase.Initialize();
                SecretStore = new WindowsUserSecretStore(secretsDirectory);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The protected local mail store could not be opened.\n\n{exception.Message}",
                "eMail MPC startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MailDatabase?.Dispose();
        base.OnExit(e);
    }
}
