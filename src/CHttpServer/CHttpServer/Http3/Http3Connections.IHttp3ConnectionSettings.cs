namespace CHttpServer.Http3;

public interface IHttp3ConnectionSettings
{
    /// <summary>
    /// Returns the maximum size of header field section that the client is prepared to accept.
    /// The size of a field list is calculated based on the uncompressed size of fields, 
    /// including the length of the name and value in bytes plus an overhead of 32 bytes for each field.
    /// <see href="https://www.rfc-editor.org/rfc/rfc9114.html#section-4.2.2"/>
    /// When this setting is not sent by the client, the default value is unlimited, which
    /// is represented by <see langword="null" />
    /// </summary>
    public ulong? ClientMaxFieldSectionSize { get; }
}

internal sealed partial class Http3Connection : IHttp3ConnectionSettings
{
    public ulong? ClientMaxFieldSectionSize { get; private set; }
}