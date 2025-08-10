export enum RequestMetadata {
    Name = 'name',
    Note = 'note',
    NoRedirect = 'no-redirect',
    Prompt = 'prompt',
    ClientsCount = 'clientscount',
    RequestCount = 'requestcount',
    NoCertificateValidation = 'no-certificate-validation',
    Timeout = 'timeout',
    KerberosAuth = 'kerberos-auth',
    SharedSocketHandler = 'shared-sockethandler',
}

export function fromString(value: string): RequestMetadata | undefined {
    value = value.toLowerCase();
    const enumName = (Object.keys(RequestMetadata) as Array<keyof typeof RequestMetadata>).find(k => RequestMetadata[k] === value);
    return enumName ? RequestMetadata[enumName] : undefined;
}