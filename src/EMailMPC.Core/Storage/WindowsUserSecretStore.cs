using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace EMailMPC.Core.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsUserSecretStore
{
    private static readonly byte[] EntropyPrefix = Encoding.UTF8.GetBytes("eMail MPC account secret v1:");

    private readonly string _directoryPath;

    public WindowsUserSecretStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
    }

    public void Save(string accountId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        EnsureWindows();

        Directory.CreateDirectory(_directoryPath);
        var plainBytes = Encoding.UTF8.GetBytes(password);
        byte[]? protectedBytes = null;
        var entropy = CreateEntropy(accountId);
        var filePath = GetFilePath(accountId);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            protectedBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(entropy);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string Load(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        EnsureWindows();

        var filePath = GetFilePath(accountId);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No protected password is stored for this account.", filePath);
        }

        var protectedBytes = File.ReadAllBytes(filePath);
        byte[]? plainBytes = null;
        var entropy = CreateEntropy(accountId);
        try
        {
            plainBytes = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            CryptographicOperations.ZeroMemory(entropy);
            if (plainBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
    }

    public void Delete(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        EnsureWindows();

        var filePath = GetFilePath(accountId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private string GetFilePath(string accountId)
    {
        var accountHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)));
        return Path.Combine(_directoryPath, $"{accountHash}.dpapi");
    }

    private static byte[] CreateEntropy(string accountId)
    {
        var accountBytes = Encoding.UTF8.GetBytes(accountId);
        var input = new byte[EntropyPrefix.Length + accountBytes.Length];
        EntropyPrefix.CopyTo(input, 0);
        accountBytes.CopyTo(input, EntropyPrefix.Length);
        try
        {
            return SHA256.HashData(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(accountBytes);
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The account secret store uses Windows DPAPI.");
        }
    }
}
