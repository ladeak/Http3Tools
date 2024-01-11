namespace CHttp.Http;

internal record SocketBehavior(
    bool EnableRedirects,
    bool EnableCertificateValidation,
    bool UseKerberosAuth,
    int MaxConnectionPerServer);
