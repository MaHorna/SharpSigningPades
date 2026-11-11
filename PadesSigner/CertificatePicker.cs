using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace PadesSigner;

/// <summary>
/// Lets the user choose the signing certificate in the standard Windows certificate dialog
/// and stores its thumbprint in appsettings.json (the running app then reloads by itself).
/// </summary>
public static class CertificatePicker
{
    private const string Title = "PAdES Signer";

    public static void ChooseAndSave(StoreLocation location)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        var all = store.Certificates;
        try
        {
            var candidates = all.Where(c => SigningCertificate.IsUsableForSigning(c, DateTime.Now)).ToList();
            if (candidates.Count == 0)
            {
                MessageBox.Show($"No valid signing certificate with a private key was found in {location}\\My.\n\n" +
                                "Import your certificate (double-click the .pfx) or plug in your token, then try again.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var thumbprint = ShowDialog(candidates);
            if (thumbprint is null) return;

            SaveThumbprint(Settings.FilePath, thumbprint, location);
            Log.Info($"Certificate {thumbprint} chosen in the certificate dialog.");
        }
        catch (Exception ex)
        {
            Log.Error("Choosing the certificate failed", ex);
            MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach (var c in all) c.Dispose();
        }
    }

    /// <summary>Shows the Windows certificate selection dialog for the given certificates; returns the chosen thumbprint.</summary>
    public static string? ShowDialog(IEnumerable<X509Certificate2> candidates)
    {
        // The dialog lists a whole store, so put just the candidates into a temporary in-memory one.
        var memStore = CertOpenStore(CERT_STORE_PROV_MEMORY, 0, IntPtr.Zero, 0, IntPtr.Zero);
        if (memStore == IntPtr.Zero) throw new Win32Exception();
        try
        {
            foreach (var cert in candidates)
            {
                if (!CertAddCertificateContextToStore(memStore, cert.Handle, CERT_STORE_ADD_ALWAYS, IntPtr.Zero))
                    throw new Win32Exception();
            }

            var picked = CryptUIDlgSelectCertificateFromStore(memStore, IntPtr.Zero, Title,
                "Choose the certificate to sign PDFs with:", 0, 0, IntPtr.Zero);
            if (picked == IntPtr.Zero) return null;
            try
            {
                using var chosen = new X509Certificate2(picked);
                return chosen.Thumbprint;
            }
            finally
            {
                CertFreeCertificateContext(picked);
            }
        }
        finally
        {
            CertCloseStore(memStore, 0);
        }
    }

    /// <summary>Writes Source/StoreThumbprint/StoreLocation in place, keeping the rest of the file (and its comments) as is.</summary>
    public static void SaveThumbprint(string settingsPath, string thumbprint, StoreLocation location)
    {
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException($"Settings file not found. Put this into Certificate.StoreThumbprint: {thumbprint}", settingsPath);
        //json read and regex parsing, to enable having comments in the settings file
        var json = File.ReadAllText(settingsPath);
        foreach (var (key, value) in new[] { ("Source", "Store"), ("StoreThumbprint", thumbprint), ("StoreLocation", location.ToString()) })
        {
            var pattern = new Regex($"(\"{key}\"\\s*:\\s*\")[^\"]*(\")"); //find the key
            if (!pattern.IsMatch(json))
                throw new InvalidOperationException($"\"{key}\" not found in {settingsPath}.");
            json = pattern.Replace(json, m => m.Groups[1].Value + value + m.Groups[2].Value, 1); //replace group with value
        }

        try
        {
            File.WriteAllText(settingsPath, json, new UTF8Encoding(false)); //save settings file after selecting cert
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"Cannot write {settingsPath} (no write access to the program folder?).\n\nPut this into Certificate.StoreThumbprint by hand: {thumbprint}");
        }
    }

    private const uint CERT_STORE_PROV_MEMORY = 2;
    private const uint CERT_STORE_ADD_ALWAYS = 4;

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern IntPtr CertOpenStore(uint storeProvider, uint encodingType, IntPtr cryptProv, uint flags, IntPtr para);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CertAddCertificateContextToStore(IntPtr store, IntPtr certContext, uint addDisposition, IntPtr storeContext);

    [DllImport("crypt32.dll")]
    private static extern bool CertFreeCertificateContext(IntPtr certContext);

    [DllImport("crypt32.dll")]
    private static extern bool CertCloseStore(IntPtr store, uint flags);

    [DllImport("cryptui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CryptUIDlgSelectCertificateFromStore(IntPtr certStore, IntPtr hwnd, string title,
        string displayString, uint dontUseColumn, uint flags, IntPtr reserved);
}
