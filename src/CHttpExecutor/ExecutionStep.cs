using System.Net;
using System.Text;
using CHttp.Data;

namespace CHttpExecutor;

public record ExecutionPlan(IEnumerable<FrozenExecutionStep> Steps, IEnumerable<Variable> Variables);

public record ExecutionStep
{
    private const int TimeoutInSeconds = 10;

    public required int LineNumber { get; init; }

    public string? Name { get; set; }

    public Uri? Uri { get; set; }

    public string? Method { get; set; }

    public StringBuilder Body { get; } = new();

    public List<KeyValueDescriptor> Headers { get; } = [];

    public Version Version { get; set; } = HttpVersion.Version20;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(TimeoutInSeconds);

    public int? ClientsCount { get; set; }

    public int? RequestsCount { get; set; }

    public bool SharedSocket { get; set; }

    public string NameOrUri() => Name ?? Uri?.ToString() ?? "missing name";

    public bool IsEmpty() =>
        Name == null && Uri == null && Method == null && Body.Length == 0
        && Headers.Count == 0 && !RequestsCount.HasValue && !ClientsCount.HasValue
        && SharedSocket == false && Timeout == TimeSpan.FromSeconds(TimeoutInSeconds)
        && Version == HttpVersion.Version20;

    // TODO Assertions
    // TODO variables
}

public class FrozenExecutionStep
{
    public static FrozenExecutionStep Default { get; } = new FrozenExecutionStep() { Method = "", Uri = new Uri("https://localhost:5001"), Version = HttpVersion.Version20, Headers = [] };

    public string? Name { get; init; }

    public required Uri Uri { get; init; }

    public required string Method { get; init; }

    public string? Body { get; init; }

    public required IReadOnlyCollection<KeyValueDescriptor> Headers { get; init; }

    public required Version Version { get; init; }

    public TimeSpan Timeout { get; init; }

    public int? ClientsCount { get; init; }

    public int? RequestsCount { get; init; }

    public bool SharedSocket { get; init; }

    public bool IsPerformanceRequest => ClientsCount.HasValue && RequestsCount.HasValue;

    // TODO Assertions
    // TODO variables
}

public record struct Variable(string Name, string Value);
