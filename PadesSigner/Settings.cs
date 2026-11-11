using System.Text.Json;

namespace PadesSigner;

public sealed class Settings
{
    public string InputFolder { get; set; } = @"%USERPROFILE%\PadesSigner\Input";
    public string OutputFolder { get; set; } = @"%USERPROFILE%\PadesSigner\Output";
    public string ProcessedFolder { get; set; } = @"%USERPROFILE%\PadesSigner\Processed";
    public string ErrorFolder { get; set; } = @"%USERPROFILE%\PadesSigner\Error";
    public string OutputSuffix { get; set; } = "_signed";
    public int RescanIntervalSeconds { get; set; } = 30;
    public CertificateSettings Certificate { get; set; } = new();
    public SignatureSettings Signature { get; set; } = new();

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Settings Load()
    {
        if (!File.Exists(FilePath))
        {
            Log.Warn($"Settings file not found at {FilePath}, using defaults.");
            return new Settings().Normalize();
        }

        // The file may still be locked by an editor that is saving it.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
                return settings.Normalize();
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(200);
            }
        }
    }

    private Settings Normalize()
    {
        InputFolder = ResolvePath(InputFolder)!;
        OutputFolder = ResolvePath(OutputFolder)!;
        ProcessedFolder = ResolvePath(ProcessedFolder) ?? "";
        ErrorFolder = ResolvePath(ErrorFolder)!;
        Certificate.PfxPath = ResolvePath(Certificate.PfxPath) ?? "";
        Certificate.PemCertPath = ResolvePath(Certificate.PemCertPath) ?? "";
        Certificate.PemKeyPath = ResolvePath(Certificate.PemKeyPath) ?? "";
        if (RescanIntervalSeconds < 5) RescanIntervalSeconds = 5;
        return this;
    }

    public static string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(expanded, AppContext.BaseDirectory);
    }
}

public sealed class CertificateSettings
{
    public string Source { get; set; } = "Store";
    public string PfxPath { get; set; } = "";
    public string PfxPassword { get; set; } = "";
    public string PemCertPath { get; set; } = "";
    public string PemKeyPath { get; set; } = "";
    public string PemKeyPassword { get; set; } = "";
    public string StoreThumbprint { get; set; } = "";
    public string StoreLocation { get; set; } = "CurrentUser";

    public System.Security.Cryptography.X509Certificates.StoreLocation Location =>
        Enum.Parse<System.Security.Cryptography.X509Certificates.StoreLocation>(StoreLocation, ignoreCase: true);
}

public sealed class SignatureSettings
{
    public string Level { get; set; } = "B-B";
    public string DigestAlgorithm { get; set; } = "SHA-256";
    public string TsaUrl { get; set; } = "";
    public string TsaUser { get; set; } = "";
    public string TsaPassword { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Location { get; set; } = "";
    public string Contact { get; set; } = "";
}
