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

    /// <summary>
    /// Sets server-driven priority parameters. Priority can be changed while the 
    /// headers are amendable or until the response has not yet been started writing.
    /// </summary>
    /// <param name="serverPriority">Irgency and Incremental parameters.</param>
    void SetPriority(Priority9218 serverPriority);
}
