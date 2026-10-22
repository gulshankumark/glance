using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Glance.Core.Layouts;

namespace Glance.Service.Security;

/// <summary>The self-signed certificate used when HTTPS is enabled, stored as https.pfx in the data directory.</summary>
public static class SelfSignedCert
{
    /// <summary>
    /// Keeps the existing https.pfx when it opens with <see cref="AppSettings.HttpsPfxPassword"/> and has not expired;
    /// otherwise creates a new one with a new random password, which is stored in <paramref name="s"/>.
    /// </summary>
    public static void EnsureCreated(GlancePaths p, AppSettings s)
    {
        if (IsUsable(p.HttpsPfx, s.HttpsPfxPassword)) return;

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        foreach (var ip in MachineAddresses.Current().Where(a => a.AddressFamily == AddressFamily.InterNetwork)) san.AddIpAddress(ip);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));   // server authentication

        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        AtomicFile.Write(p.HttpsPfx, cert.Export(X509ContentType.Pfx, password));
        s.HttpsPfxPassword = password;
    }

    internal static bool IsUsable(string path, string? password)
    {
        if (password is null || !File.Exists(path)) return false;
        try
        {
            using var cert = new X509Certificate2(path, password, X509KeyStorageFlags.EphemeralKeySet);
            return cert.HasPrivateKey && cert.NotAfter > DateTime.Now;
        }
        catch (CryptographicException) { return false; }
    }
}
