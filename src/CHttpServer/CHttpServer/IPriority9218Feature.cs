namespace CHttpServer;

/// <summary>
/// Priority feature to manage the urgency and incremental
/// flags of STREAM priorty defined by RFC9218.
/// </summary>
public interface IPriority9218Feature
{
    /// <summary>
    /// Gets the urgency and incremental values requested by the client.
    /// </summary>
    Priority9218 Priority { get; }
}
