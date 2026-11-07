using System.Security.Cryptography;
using System.Text;
using FlowAI.Core.Configuration;

namespace FlowAI.AI.Security;

/// <summary>
/// Secure credential storage using Windows Data Protection API (DPAPI).
/// API keys are encrypted per Windows user account with machine-level salt.
/// </summary>
public static class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FlowAI.Windows.CredentialEntropy.2026");

    private static string GetKeyFilePath(string providerName) =>
        Path.Combine(FlowAISettings.AppDataDirectory, $"{providerName.ToLowerInvariant()}_secure.dat");

    public static void SaveSecret(string providerName, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            DeleteSecret(providerName);
            return;
        }

        Directory.CreateDirectory(FlowAISettings.AppDataDirectory);
        byte[] plainBytes = Encoding.UTF8.GetBytes(secret);
        byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetKeyFilePath(providerName), encryptedBytes);
    }

    public static string? LoadSecret(string providerName)
    {
        string filePath = GetKeyFilePath(providerName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            byte[] encryptedBytes = File.ReadAllBytes(filePath);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to decrypt secret for {providerName}: {ex.Message}");
            return null;
        }
    }

    public static void DeleteSecret(string providerName)
    {
        string filePath = GetKeyFilePath(providerName);
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch { }
        }
    }
}
