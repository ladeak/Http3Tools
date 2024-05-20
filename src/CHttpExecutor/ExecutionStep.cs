using System.Net;
using CHttp.Data;

namespace CHttpExecutor;

public record ExecutionPlan(IEnumerable<FrozenExecutionStep> Steps, IEnumerable<Variable> Variables);

public record class VarValue<T>
    where T : ISpanParsable<T>
{
    public static bool TryCreate(ReadOnlySpan<char> source, out VarValue<T> value)
    {
        if (T.TryParse(source, null, out var parsed))
        {
            value = new VarValue<T>(parsed);
            return true;
        }
        value = new VarValue<T>(source.ToString());
        return true;
    }

    public VarValue(T value)
    {
        Value = value;
    }

    public VarValue(string variableValue)
    {
        VariableValue = variableValue;
    }

    public T? Value { get; }

    public string? VariableValue { get; }

    public bool HasValue => VariableValue == null;
}

public static class VarValue
{
    public static VarValue<bool> True = new VarValue<bool>(true);
    public static VarValue<bool> False = new VarValue<bool>(false);
}

public class ExecutionStep
{
    private static VarValue<TimeSpan> DefaultTimeout = new VarValue<TimeSpan>(TimeSpan.FromSeconds(TimeoutInSeconds));

    private const int TimeoutInSeconds = 10;

    public required int LineNumber { get; init; }

    public string? Name { get; set; }

    public string? Uri { get; set; }

    public string? Method { get; set; }

    public List<string> Body { get; } = [];

    public List<KeyValueDescriptor> Headers { get; } = [];

    public Version Version { get; set; } = HttpVersion.Version20;

    public VarValue<TimeSpan> Timeout { get; set; } = DefaultTimeout;

    public VarValue<int>? ClientsCount { get; set; }

    public VarValue<int>? RequestsCount { get; set; }

    public VarValue<bool> SharedSocket { get; set; } = VarValue.False;

    public VarValue<bool> EnableRedirects { get; set; } = VarValue.True;

    public VarValue<bool> NoCertificateValidation { get; set; } = VarValue.False;

    public string NameOrUri() => Name ?? Uri?.ToString() ?? "missing name";

    public bool IsDefault() =>
        Name == null && Uri == null && Method == null && Body.Count == 0
        && EnableRedirects == VarValue.True && NoCertificateValidation == VarValue.False
        && Headers.Count == 0 && RequestsCount == null && ClientsCount == null
        && SharedSocket == VarValue.False && Timeout == DefaultTimeout
        && Version == HttpVersion.Version20;

    // TODO Assertions
}

public class FrozenExecutionStep
{
    public required int LineNumber { get; init; }

    public string? Name { get; init; }

    public required string Uri { get; init; }

    public required string Method { get; init; }

    public required IReadOnlyCollection<string> Body { get; init; }

    public required IReadOnlyCollection<KeyValueDescriptor> Headers { get; init; }

    public required Version Version { get; init; }

    public required VarValue<TimeSpan> Timeout { get; init; }

    public VarValue<int>? ClientsCount { get; init; }

    public VarValue<int>? RequestsCount { get; init; }

    public VarValue<bool> SharedSocket { get; init; } = VarValue.False;

    public VarValue<bool> EnableRedirects { get; set; } = VarValue.True;

    public VarValue<bool> NoCertificateValidation { get; set; } = VarValue.False;

    public bool IsPerformanceRequest => ClientsCount != null && RequestsCount != null;

    // TODO Assertions
}

public record struct Variable(string Name, string Value);
