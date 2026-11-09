using System.Security.Cryptography;
using System.Text;

namespace HVAKR.Api;

/// <summary>
/// Keeps the HVAKR API key between Revit sessions. The key is encrypted with Windows DPAPI in
/// the current-user scope, so only the same Windows account on the same machine can read it.
/// The file is never plain text and never leaves <c>%LOCALAPPDATA%\HVAKR</c>.
/// </summary>
public sealed class CredentialStore(string? rootDirectory = null)
{
    // Ties the ciphertext to this purpose, so another DPAPI blob of the same user cannot be swapped in.
    private static readonly byte[] Entropy = "HVAKR.Revit.ApiKey"u8.ToArray();

    public string FilePath { get; } = Path.Combine(
        rootDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HVAKR"),
        "credentials.bin");

    public string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var apiKey = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        }
        catch (Exception ex)
        {
            // A corrupt file or one from another Windows account cannot be decrypted. Drop it so the user logs in again.
            Logger.LogError("Failed to load the saved HVAKR API key", ex);
            Clear();
            return null;
        }
    }

    public void Save(string apiKey)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllBytes(
                temporaryPath,
                ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporaryPath, FilePath, true);
        }
        catch (Exception ex)
        {
            // The login itself worked; the user only has to log in again next session.
            Logger.LogError("Failed to save the HVAKR API key", ex);
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to delete the saved HVAKR API key", ex);
        }
    }
}
