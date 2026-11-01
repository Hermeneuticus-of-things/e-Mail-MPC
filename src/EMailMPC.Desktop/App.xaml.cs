using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    public string DataDirectory { get; private set; } = string.Empty;
    public string DatabaseKeyPath { get; private set; } = string.Empty;
    public string AccountSecretsDirectory { get; private set; } = string.Empty;
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var userScope = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var mutexName = $"Local\\eMailMPC-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userScope)))}";
            _instanceMutex = new Mutex(initiallyOwned: false, mutexName);
            try
            {
                _ownsInstanceMutex = _instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                _ownsInstanceMutex = true;
            }

            if (!_ownsInstanceMutex)
            {
                MessageBox.Show(
                    "eMail MPC is already running for this Windows user. Use the existing window before restoring local data.",
                    "eMail MPC is already running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(1);
                return;
            }

            var dataDirectory = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"))
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "eMail MPC",
                    "data");
            Directory.CreateDirectory(dataDirectory);
            var keyPath = Path.Combine(dataDirectory, "keys", "database.dpapi");
            var databasePath = Path.Combine(dataDirectory, "mail.db");
            var secretsDirectory = Path.Combine(dataDirectory, "keys", "accounts");
            DataDirectory = Path.GetFullPath(dataDirectory);
            DatabaseKeyPath = Path.GetFullPath(keyPath);
            AccountSecretsDirectory = Path.GetFullPath(secretsDirectory);
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
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
            _ownsInstanceMutex = false;
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
