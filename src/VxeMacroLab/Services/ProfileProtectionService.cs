using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VxeMacroLab.Services;

/// <summary>
/// Encrypts profile files with Windows DPAPI, bound to the current Windows user.
/// No plaintext profile is written to a temporary file.
/// </summary>
public static class ProfileProtectionService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VXEP1");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VXE-Macro-Lab/Profile/v1");

    public static void Save<T>(string path, T profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(profile);

        byte[]? plaintext = null;
        byte[]? protectedPayload = null;
        byte[]? output = null;
        string? temporaryPath = null;

        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(profile, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            protectedPayload = ProtectedData.Protect(
                plaintext, Entropy, DataProtectionScope.CurrentUser);

            output = new byte[Magic.Length + protectedPayload.Length];
            Buffer.BlockCopy(Magic, 0, output, 0, Magic.Length);
            Buffer.BlockCopy(protectedPayload, 0, output, Magic.Length, protectedPayload.Length);

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Не удалось определить папку профиля.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, "." + Path.GetRandomFileName() + ".tmp");

            using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                stream.Write(output, 0, output.Length);
                stream.Flush(true);
            }

            File.Move(temporaryPath, fullPath, true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { /* Best-effort cleanup. */ }
            }

            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (protectedPayload is not null) CryptographicOperations.ZeroMemory(protectedPayload);
            if (output is not null) CryptographicOperations.ZeroMemory(output);
        }
    }

    public static T Load<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] fileBytes = File.ReadAllBytes(path);
        byte[]? plaintext = null;

        try
        {
            if (fileBytes.Length > Magic.Length &&
                fileBytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                plaintext = ProtectedData.Unprotect(
                    fileBytes.AsSpan(Magic.Length).ToArray(),
                    Entropy,
                    DataProtectionScope.CurrentUser);
            }
            else
            {
                // Backward compatibility: allow importing older unencrypted JSON profiles.
                plaintext = fileBytes.ToArray();
            }

            return JsonSerializer.Deserialize<T>(plaintext)
                ?? throw new InvalidDataException("Файл профиля пуст или повреждён.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileBytes);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
