namespace Loupedeck.VoxRingPlugin.Helpers;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// DPAPI-backed at-rest encryption for API keys, webhook URLs, and other sensitive strings
/// persisted via <c>SetPluginSetting</c>.
///
/// <para>Threat model + guarantees:</para>
/// <list type="bullet">
///   <item>Ciphertext is bound to the current Windows user on the current machine
///         (<see cref="DataProtectionScope.CurrentUser"/>). Copying settings files to another
///         user or machine yields unusable bytes.</item>
///   <item>A plugin-specific <see cref="Entropy"/> salt binds ciphertext to this plugin, so
///         another app running under the same user can't decrypt our values even with its own
///         DPAPI access.</item>
///   <item>Ciphertext is tagged with <see cref="EncryptedPrefix"/>. Untagged values are treated
///         as legacy plaintext (users who saved keys before this change) and returned as-is;
///         callers are expected to re-save them so they get upgraded to the tagged encrypted form.</item>
/// </list>
///
/// <para>What this does NOT protect against:</para>
/// <list type="bullet">
///   <item>A process running under the same logged-in Windows user account. DPAPI is designed
///         to make this work by default, so any user-level code can decrypt. Raising the bar
///         further (e.g. Credential Manager or a user password) is possible but requires UX
///         changes and is out of scope for this layer.</item>
/// </list>
/// </summary>
internal static class SecureStore
{
    /// <summary>Tag for DPAPI-encrypted payloads. Untagged stored values are treated as plaintext.</summary>
    public const string EncryptedPrefix = "enc:v1:";

    /// <summary>
    /// Plugin-specific entropy. Changing this string invalidates every previously-stored secret.
    /// Never change unless you also force users to re-enter their keys.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Loupedeck.VoxRingPlugin.Secret.v1");

    /// <summary>
    /// Encrypt and tag the given plaintext. Returns an empty string for null/empty input so
    /// callers can safely "clear" a setting by saving an empty value.
    /// On non-Windows (DPAPI unavailable) returns the plaintext untouched, so cross-platform
    /// builds still compile and run — but secrets are NOT protected there.
    /// </summary>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (!OperatingSystem.IsWindows()) return plaintext;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return EncryptedPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex)
        {
            // If encryption fails we'd rather return plaintext than silently drop the value.
            // The caller still persists *something*, and we log the reason for investigation.
            PluginLog.Warning(ex, "SecureStore.Protect failed, falling back to plaintext");
            return plaintext;
        }
    }

    /// <summary>
    /// Decrypt a tagged value. If the input is untagged (legacy plaintext), returns it unchanged
    /// so existing user secrets still work; the caller should re-save to migrate to the
    /// encrypted form. Returns empty string on decryption failure (wrong user, corrupt data).
    /// </summary>
    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;

        // Legacy plaintext — return as-is. The save-side will re-encrypt on next write.
        if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return stored;

        if (!OperatingSystem.IsWindows())
        {
            PluginLog.Warning("SecureStore.Unprotect called on non-Windows; ciphertext is undecryptable here");
            return string.Empty;
        }

        try
        {
            var payload = stored.Substring(EncryptedPrefix.Length);
            var protectedBytes = Convert.FromBase64String(payload);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            // Typical causes: ciphertext from a different user/machine, entropy mismatch (version bump),
            // or tampered storage. We intentionally don't throw — we return empty so the user can re-enter.
            PluginLog.Warning(ex, "SecureStore.Unprotect failed; caller will see empty value");
            return string.Empty;
        }
        catch (FormatException ex)
        {
            PluginLog.Warning(ex, "SecureStore.Unprotect: stored value is not valid base64");
            return string.Empty;
        }
    }

    /// <summary>True if the stored value is untagged plaintext that should be migrated on next save.</summary>
    public static bool IsLegacyPlaintext(string stored) =>
        !string.IsNullOrEmpty(stored) && !stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
}
