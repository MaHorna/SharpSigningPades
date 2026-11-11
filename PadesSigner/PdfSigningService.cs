using iText.Kernel.Pdf;
using iText.Signatures;

namespace PadesSigner;

/// <summary>Applies a PAdES baseline signature (ETSI.CAdES.detached) to a PDF.</summary>
public sealed class PdfSigningService : IDisposable
{
    private readonly SigningCertificate _certificate;
    private readonly SignatureSettings _cfg;
    private readonly IExternalSignature _externalSignature;

    public string Description { get; }

    public PdfSigningService(SigningCertificate certificate, SignatureSettings cfg)
    {
        _certificate = certificate;
        _cfg = cfg;
        _externalSignature = certificate.CreateExternalSignature(cfg.DigestAlgorithm);

        if (NormalizedLevel != "B-B" && string.IsNullOrWhiteSpace(cfg.TsaUrl))
            throw new InvalidOperationException($"Signature level {cfg.Level} requires Signature.TsaUrl.");

        // Otherwise every single file would fail while fetching revocation data.
        if (NormalizedLevel is "B-LT" or "B-LTA" && !HasRevocationSource(certificate.Certificate))
            throw new InvalidOperationException(
                $"Signature level {cfg.Level} needs revocation data, but the certificate has no OCSP or CRL address. Use B-T.");

        Description = $"PAdES {NormalizedLevel}, {certificate.Certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}";
    }

    private string NormalizedLevel => _cfg.Level.Trim().ToUpperInvariant().Replace("_", "-") switch
    {
        "B" or "BB" or "B-B" => "B-B",
        "T" or "BT" or "B-T" => "B-T",
        "LT" or "BLT" or "B-LT" => "B-LT",
        "LTA" or "BLTA" or "B-LTA" => "B-LTA",
        _ => throw new InvalidOperationException($"Unknown Signature.Level '{_cfg.Level}'. Use B-B, B-T, B-LT or B-LTA."),
    };

    public void Sign(string inputPath, string outputPath)
    {
        using var reader = new PdfReader(inputPath);
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);

        // Append mode keeps the original bytes (and any existing signatures) intact.
        var signer = new PdfPadesSigner(reader, output)
            .SetStampingProperties(new StampingProperties().UseAppendMode());

        var props = new SignerProperties()
            .SetFieldName($"Signature_{DateTime.UtcNow:yyyyMMddHHmmssfff}")
            .SetSignatureCreator("PadesSigner");
        if (!string.IsNullOrWhiteSpace(_cfg.Reason)) props.SetReason(_cfg.Reason);
        if (!string.IsNullOrWhiteSpace(_cfg.Location)) props.SetLocation(_cfg.Location);
        if (!string.IsNullOrWhiteSpace(_cfg.Contact)) props.SetContact(_cfg.Contact);

        var chain = _certificate.Chain;
        switch (NormalizedLevel)
        {
            case "B-B":
                signer.SignWithBaselineBProfile(props, chain, _externalSignature);
                break;
            case "B-T":
                signer.SignWithBaselineTProfile(props, chain, _externalSignature, CreateTsaClient());
                break;
            case "B-LT":
                signer.SignWithBaselineLTProfile(props, chain, _externalSignature, CreateTsaClient());
                break;
            case "B-LTA":
                signer.SignWithBaselineLTAProfile(props, chain, _externalSignature, CreateTsaClient());
                break;
        }
    }

    private static bool HasRevocationSource(System.Security.Cryptography.X509Certificates.X509Certificate2 cert)
    {
        const string CrlDistributionPoints = "2.5.29.31", AuthorityInfoAccess = "1.3.6.1.5.5.7.1.1";
        return cert.Extensions.Any(e => e.Oid?.Value == CrlDistributionPoints) ||
               cert.Extensions.Where(e => e.Oid?.Value == AuthorityInfoAccess)
                   .Any(e => new System.Security.Cryptography.X509Certificates.X509AuthorityInformationAccessExtension(e.RawData).EnumerateOcspUris().Any());
    }

    private ITSAClient CreateTsaClient() => string.IsNullOrEmpty(_cfg.TsaUser)
        ? new TSAClientBouncyCastle(_cfg.TsaUrl)
        : new TSAClientBouncyCastle(_cfg.TsaUrl, _cfg.TsaUser, _cfg.TsaPassword);

    public void Dispose() => _certificate.Dispose();
}
