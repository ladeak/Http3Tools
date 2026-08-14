using System.Security.Cryptography.X509Certificates;

namespace CHttp.Http;

internal record SocketBehavior(
    bool EnableRedirects,
    bool EnableCertificateValidation,
    bool UseKerberosAuth,
    int MaxConnectionPerServer,
    bool AutomaticDecompression,
    X509Certificate2? ClientCertificate);
