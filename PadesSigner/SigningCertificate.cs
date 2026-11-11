using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.Signatures;

namespace PadesSigner;

/// <summary>
/// Signing certificate with its private key and chain, loaded from a PFX, PEM files or the Windows store.
/// The key is used through .NET (CNG), so non-exportable store keys and smart cards work too.
/// </summary>
public sealed class SigningCertificate : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public IX509Certificate[] Chain { get; }
    private readonly X509Certificate2Collection _owned;

    private SigningCertificate(X509Certificate2 certificate, X509Certificate2Collection extraCerts, X509Certificate2Collection owned)
    {
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException($"Certificate '{certificate.Subject}' has no private key.");

        Certificate = certificate;
        Chain = BuildChain(certificate, extraCerts);
        _owned = owned;
    }

    public static SigningCertificate Load(CertificateSettings cfg)
    {
        return cfg.Source.Trim().ToLowerInvariant() switch
        {
            "pfx" or "p12" or "pkcs12" => LoadPfx(cfg),
            "pem" => LoadPem(cfg),
            "store" => LoadFromStore(cfg),
            _ => throw new InvalidOperationException($"Unknown Certificate.Source '{cfg.Source}'. Use Pfx, Pem or Store."),
        };
    }

    private static SigningCertificate LoadPfx(CertificateSettings cfg)
    {
        RequireFile(cfg.PfxPath, "Certificate.PfxPath");
        var password = string.IsNullOrEmpty(cfg.PfxPassword)
            ? Environment.GetEnvironmentVariable("PADES_SIGNER_PASSWORD")
            : cfg.PfxPassword;

        var all = X509CertificateLoader.LoadPkcs12CollectionFromFile(
            cfg.PfxPath, password, X509KeyStorageFlags.EphemeralKeySet);
        var signer = all.FirstOrDefault(c => c.HasPrivateKey)
            ?? throw new InvalidOperationException($"No certificate with a private key found in {cfg.PfxPath}.");
        var extra = new X509Certificate2Collection(all.Where(c => c != signer).ToArray());
        return new SigningCertificate(signer, extra, all);
    }

    private static SigningCertificate LoadPem(CertificateSettings cfg)
    {
        RequireFile(cfg.PemCertPath, "Certificate.PemCertPath");
        RequireFile(cfg.PemKeyPath, "Certificate.PemKeyPath");

        var withKey = string.IsNullOrEmpty(cfg.PemKeyPassword)
            ? X509Certificate2.CreateFromPemFile(cfg.PemCertPath, cfg.PemKeyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(cfg.PemCertPath, cfg.PemKeyPassword, cfg.PemKeyPath);

        // The certificate file may also carry intermediates.
        var all = new X509Certificate2Collection();
        all.ImportFromPemFile(cfg.PemCertPath);
        var extra = new X509Certificate2Collection(all.Where(c => c.Thumbprint != withKey.Thumbprint).ToArray());
        all.Add(withKey);
        return new SigningCertificate(withKey, extra, all);
    }

    private static SigningCertificate LoadFromStore(CertificateSettings cfg)
    {
        var location = cfg.Location;
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        var all = store.Certificates;

        X509Certificate2 cert;
        if (string.IsNullOrWhiteSpace(cfg.StoreThumbprint))
        {
            cert = PickSigningCertificate(all, DateTime.Now, $"{location}\\My");
            Log.Info($"No thumbprint configured, picked '{cert.Subject}' ({cert.Thumbprint}) automatically.");
        }
        else
        {
            // Copying from the certmgr dialog adds spaces and an invisible mark; keep only the hex digits.
            var thumbprint = new string(cfg.StoreThumbprint.Where(char.IsAsciiHexDigit).ToArray());
            cert = all.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).FirstOrDefault()
                ?? throw new InvalidOperationException($"Certificate {thumbprint} not found in {location}\\My.");
        }
        return new SigningCertificate(cert, [], all);
    }

    // Extended key usages that are accepted for document signatures.
    private static readonly HashSet<string> SigningEkus =
    [
        "1.3.6.1.5.5.7.3.4",        // Secure e-mail (what most personal / qualified certificates carry)
        "1.3.6.1.5.5.7.3.36",       // Document signing (RFC 9336)
        "1.3.6.1.4.1.311.10.3.12",  // Microsoft document signing
        "1.2.840.113583.1.1.5",     // Adobe Authentic Documents Trust
        "2.5.29.37.0",              // Any extended key usage
    ];

    /// <summary>True for a currently valid certificate with a private key that is allowed to sign documents.</summary>
    public static bool IsUsableForSigning(X509Certificate2 cert, DateTime now)
    {
        if (!cert.HasPrivateKey || now < cert.NotBefore || now > cert.NotAfter) return false;

        var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is not null &&
            (keyUsage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
            return false;

        // No EKU extension means the certificate is not restricted to particular uses.
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        return eku is null || eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => SigningEkus.Contains(o.Value!));
    }

    /// <summary>
    /// Picks the one signing certificate in the store. Renewed certificates (same subject and issuer) count as one,
    /// the newest wins. Several different identities are ambiguous and require an explicit thumbprint.
    /// </summary>
    public static X509Certificate2 PickSigningCertificate(IEnumerable<X509Certificate2> certs, DateTime now, string storeName)
    {
        var candidates = certs
            .Where(c => IsUsableForSigning(c, now))
            .GroupBy(c => (c.Subject, c.Issuer))
            .Select(g => g.OrderByDescending(c => c.NotAfter).First())
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"No valid signing certificate with a private key found in {storeName}. Import one or set Certificate.StoreThumbprint."),
            _ => throw new InvalidOperationException(
                $"{candidates.Count} signing certificates found in {storeName}; right-click the tray icon > Choose certificate: " +
                string.Join("; ", candidates.Select(c => $"{c.GetNameInfo(X509NameType.SimpleName, false)} = {c.Thumbprint}"))),
        };
    }

    private static void RequireFile(string path, string setting)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{setting} is not set.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"{setting} points to a missing file.", path);
    }

    private static IX509Certificate[] BuildChain(X509Certificate2 cert, X509Certificate2Collection extra)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.ExtraStore.AddRange(extra);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
        chain.Build(cert);

        var certs = chain.ChainElements.Select(e => e.Certificate).ToList();
        if (certs.Count == 0 || certs[0].Thumbprint != cert.Thumbprint)
            certs = [cert, .. extra];

        var parser = new Org.BouncyCastle.X509.X509CertificateParser();
        return certs
            .Select(c => (IX509Certificate)new X509CertificateBC(parser.ReadCertificate(c.RawData)))
            .ToArray();
    }

    public IExternalSignature CreateExternalSignature(string digestAlgorithm) =>
        new DotNetExternalSignature(Certificate, digestAlgorithm);

    public void Dispose()
    {
        foreach (var c in _owned) c.Dispose();
    }
}

/// <summary>Bridges iText's signing callback to the .NET RSA/ECDSA private key.</summary>
internal sealed class DotNetExternalSignature : IExternalSignature
{
    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;
    private readonly string _digestName;
    private readonly HashAlgorithmName _hash;

    public DotNetExternalSignature(X509Certificate2 cert, string digestAlgorithm)
    {
        (_digestName, _hash) = digestAlgorithm.Replace("-", "").ToUpperInvariant() switch
        {
            "SHA256" => ("SHA-256", HashAlgorithmName.SHA256),
            "SHA384" => ("SHA-384", HashAlgorithmName.SHA384),
            "SHA512" => ("SHA-512", HashAlgorithmName.SHA512),
            _ => throw new InvalidOperationException($"Unsupported digest algorithm '{digestAlgorithm}'. Use SHA-256, SHA-384 or SHA-512."),
        };

        _rsa = cert.GetRSAPrivateKey();
        _ecdsa = _rsa is null ? cert.GetECDsaPrivateKey() : null;
        if (_rsa is null && _ecdsa is null)
            throw new InvalidOperationException("Only RSA and ECDSA keys are supported.");
    }

    public string GetDigestAlgorithmName() => _digestName;

    public string GetSignatureAlgorithmName() => _rsa is not null ? "RSA" : "ECDSA";

    public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;

    public byte[] Sign(byte[] message) => _rsa is not null
        ? _rsa.SignData(message, _hash, RSASignaturePadding.Pkcs1)
        : _ecdsa!.SignData(message, _hash, DSASignatureFormat.Rfc3279DerSequence);
}
