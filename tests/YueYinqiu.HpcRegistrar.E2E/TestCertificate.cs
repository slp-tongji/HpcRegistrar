using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace YueYinqiu.HpcRegistrar.E2E;

public sealed class TestCertificate : IDisposable
{
    public string RootCaPem { get; }

    public X509Certificate2 ServerCertificate { get; }

    private readonly X509Certificate2 _rootCa;

    private TestCertificate(string rootCaPem, X509Certificate2 serverCertificate, X509Certificate2 rootCa)
    {
        RootCaPem = rootCaPem;
        ServerCertificate = serverCertificate;
        _rootCa = rootCa;
    }

    public static TestCertificate Create(string directory)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=e2e-test-ca",
            rootKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));

        using var rootCert = rootRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));

        var rootPem = PemEncoding.Write("CERTIFICATE", rootCert.RawData);
        Directory.CreateDirectory(directory);
        var rootPemPath = Path.Combine(directory, "oidc-ca.pem");
        File.WriteAllText(rootPemPath, new string(rootPem));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            "CN=localhost",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        leafRequest.CertificateExtensions.Add(san.Build());

        leafRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        leafRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                true));

        var leafSerial = new byte[16];
        RandomNumberGenerator.Fill(leafSerial);
        leafSerial[0] &= 0x7F;

        using var leafCert = leafRequest.Create(
            rootCert,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            leafSerial);

        var leafPfx = X509CertificateLoader.LoadPkcs12(
            leafCert.CopyWithPrivateKey(leafKey).Export(X509ContentType.Pfx),
            null,
            X509KeyStorageFlags.Exportable);

        var rootCa = X509CertificateLoader.LoadPkcs12(
            rootCert.Export(X509ContentType.Pfx),
            null,
            X509KeyStorageFlags.Exportable);

        return new TestCertificate(rootPemPath, leafPfx, rootCa);
    }

    public HttpClientHandler CreateTrustingHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null)
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.CustomTrustStore.Add(_rootCa);
                return chain.Build(new X509Certificate2(cert));
            },
        };
    }

    public void Dispose()
    {
        ServerCertificate.Dispose();
        _rootCa.Dispose();
    }
}
