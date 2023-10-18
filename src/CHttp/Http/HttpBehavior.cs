namespace CHttp;

internal record HttpBehavior(
	bool EnableRedirects, 
	bool EnableCertificateValidation, 
	double Timeout,
	bool ToUtf8,
	string CookieContainer,
	bool UseKerberosAuth);
