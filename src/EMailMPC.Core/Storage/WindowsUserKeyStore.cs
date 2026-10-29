using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace EMailMPC.Core.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsUserKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("eMail MPC local database key v1");

    public byte[] LoadOrCreate(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        EnsureWindows();

        var fullPath = Path.GetFullPath(keyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath))
        {
            return Unprotect(File.ReadAllBytes(fullPath));
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(protectedKey);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, fullPath);
            }
            catch (IOException) when (File.Exists(fullPath))
            {
                CryptographicOperations.ZeroMemory(key);
                return Unprotect(File.ReadAllBytes(fullPath));
            }

            return key;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static byte[] Unprotect(byte[] protectedKey)
    {
        try
        {
            var key = ProtectedData.Unprotect(protectedKey, Entropy, DataProtectionScope.CurrentUser);
            if (key.Length != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("The protected database key has an invalid length.");
            }

            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The current user key store uses Windows DPAPI.");
        }
    }
}
