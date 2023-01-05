using CHttp.Data;

internal record HttpBehavior(bool EnableRedirects, bool EnableCertificateValidation, LogLevel LogLevel);
