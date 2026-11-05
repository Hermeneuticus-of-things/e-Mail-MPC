using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using EMailMPC.Core.Storage;
using EMailMPC.Core.Updates;

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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0] == "--apply-portable-update")
        {
            await RunPortableUpdateHelperAsync(e.Args);
            return;
        }

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
        try
        {
            WriteUpdateHealthMarker(e.Args);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The updated application could not complete its startup check.\n\n{exception.Message}",
                "eMail MPC startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteUpdateHealthMarker(IReadOnlyList<string> arguments)
    {
        var markerArgumentIndex = -1;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--update-health-marker")
            {
                markerArgumentIndex = index;
                break;
            }
        }

        if (markerArgumentIndex < 0)
        {
            return;
        }

        if (markerArgumentIndex + 1 >= arguments.Count)
        {
            throw new ArgumentException("The update health marker path is missing.");
        }

        var markerPath = Path.GetFullPath(arguments[markerArgumentIndex + 1]);
        var updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eMail MPC",
            "updates");
        if (!IsPathWithin(markerPath, updatesDirectory))
        {
            throw new InvalidDataException("The update health marker path is outside the update staging directory.");
        }

        File.WriteAllText(markerPath, "started");
    }

    private async Task RunPortableUpdateHelperAsync(IReadOnlyList<string> arguments)
    {
        string? backupDirectory = null;
        string? healthMarkerPath = null;
        Process? updatedProcess = null;
        var installationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        try
        {
            if (arguments.Count != 5
                || !int.TryParse(arguments[2], out var parentProcessId)
                || parentProcessId <= 0
                || !long.TryParse(arguments[4], out var expectedSizeBytes))
            {
                throw new ArgumentException("The portable update command arguments are invalid.");
            }

            var archivePath = Path.GetFullPath(arguments[1]);
            var updatesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eMail MPC",
                "updates");
            if (!IsPathWithin(archivePath, updatesDirectory))
            {
                throw new InvalidDataException("The update archive is outside the eMail MPC staging directory.");
            }

            Process? parentProcess = null;
            try
            {
                parentProcess = Process.GetProcessById(parentProcessId);
            }
            catch (ArgumentException)
            {
            }

            if (parentProcess is not null)
            {
                using (parentProcess)
                {
                    var parentExecutable = parentProcess.MainModule?.FileName;
                    if (!string.Equals(
                            parentExecutable,
                            Path.Combine(installationDirectory, "EMailMPC.Desktop.exe"),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The update parent process is not this portable installation.");
                    }

                    await parentProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
                }
            }

            backupDirectory = await new PortableUpdateService().ApplyAsync(
                archivePath,
                installationDirectory,
                arguments[3],
                expectedSizeBytes);

            healthMarkerPath = Path.Combine(updatesDirectory, $"health-{Guid.NewGuid():N}.marker");
            var updatedExecutable = Path.Combine(installationDirectory, "EMailMPC.Desktop.exe");
            updatedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = updatedExecutable,
                UseShellExecute = false,
                ArgumentList = { "--update-health-marker", healthMarkerPath }
            }) ?? throw new InvalidOperationException("The updated application process could not be started.");

            var startupDeadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (DateTimeOffset.UtcNow < startupDeadline)
            {
                if (File.Exists(healthMarkerPath))
                {
                    var cleanupWarning = PortableUpdateService.Complete(installationDirectory, backupDirectory);
                    backupDirectory = null;
                    File.Delete(healthMarkerPath);
                    if (cleanupWarning is not null)
                    {
                        MessageBox.Show(
                            cleanupWarning,
                            "Portable update completed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    Shutdown(0);
                    return;
                }

                if (updatedProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        $"The updated application exited during startup with code {updatedProcess.ExitCode}.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            throw new TimeoutException("The updated application did not complete its startup check.");
        }
        catch (Exception exception)
        {
            if (updatedProcess is { HasExited: false })
            {
                updatedProcess.Kill(entireProcessTree: true);
                await updatedProcess.WaitForExitAsync();
            }

            Exception failure = exception;
            if (backupDirectory is not null)
            {
                try
                {
                    PortableUpdateService.Rollback(installationDirectory, backupDirectory);
                    backupDirectory = null;
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(installationDirectory, "EMailMPC.Desktop.exe"),
                        UseShellExecute = true
                    });
                }
                catch (Exception rollbackException)
                {
                    failure = new AggregateException(
                        $"The portable update failed and automatic rollback was unsuccessful. Recovery files are at '{backupDirectory}'.",
                        exception,
                        rollbackException);
                }
            }

            MessageBox.Show(
                $"The portable update could not be completed.\n\n{failure.Message}",
                "Portable update rolled back",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
        finally
        {
            updatedProcess?.Dispose();
            if (healthMarkerPath is not null && File.Exists(healthMarkerPath))
            {
                File.Delete(healthMarkerPath);
            }
        }
    }

    private static bool IsPathWithin(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
