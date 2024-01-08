namespace CHttp;

internal record SocketBehavior(
    bool EnableRedirects,
    bool EnableCertificateValidation,
    bool UseKerberosAuth,
    int MaxConnectionPerServer);
