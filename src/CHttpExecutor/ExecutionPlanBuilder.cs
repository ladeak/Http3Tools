using System.Buffers;
using System.CommandLine.IO;
using System.Net;
using System.Text;
using CHttp.Data;

namespace CHttpExecutor;

public interface IExecutionPlanBuilder
{
    ExecutionPlan Build();

    void AddStep(int lineNumber);

    void AddMethod(string requestVerb);

    void AddUri(ReadOnlySpan<char> uri);

    void AddHttpVersion(ReadOnlySpan<char> version);

    void AddVariable(ReadOnlySpan<char> variable, ReadOnlySpan<char> value);

    void AddBodyLine(ReadOnlySpan<char> line);

    void AddHeader(ReadOnlySpan<char> name, ReadOnlySpan<char> value);

    void AddExecutionInstruction(ReadOnlySpan<char> command, ReadOnlySpan<char> parameters);

    void AddExecutionInstruction(ReadOnlySpan<char> command);
}

public partial class ExecutionPlanBuilder : IExecutionPlanBuilder
{
    private static CompositeFormat ErrorFormat = CompositeFormat.Parse("Line {0}, with {1} {2}");

    private List<FrozenExecutionStep> _steps = new();

    private List<Variable> _variables = new();

    private ExecutionStep _currentStep = new() { LineNumber = 1 };

    public void AddStep(int lineNumber)
    {
        if (!_currentStep.IsDefault())
            _steps.Add(FreezeCurrentStep(_currentStep));
        _currentStep = new() { LineNumber = lineNumber };
    }

    public void AddMethod(string requestVerb)
    {
        _currentStep.Method = requestVerb;
    }

    public void AddUri(ReadOnlySpan<char> rawUri)
    {
        if (rawUri.Length == 0)
            throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.NameOrUri(), "Uri is missing"));
        _currentStep.Uri = rawUri.ToString();
    }

    public void AddHttpVersion(ReadOnlySpan<char> version)
    {
        _currentStep.Version = version switch
        {
            "1" => HttpVersion.Version11,
            "1.1" => HttpVersion.Version11,
            "2" => HttpVersion.Version20,
            "3" => HttpVersion.Version30,
            _ => throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "Invalid HTTP version"))
        };
    }

    public void AddVariable(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        if (value.StartsWith("{{$") && value.EndsWith("}}"))
            value = Environment.GetEnvironmentVariable(value[3..^3].ToString());
        _variables.Add(new(name.ToString(), value.ToString()));
    }

    public void AddBodyLine(ReadOnlySpan<char> line)
    {
        _currentStep.Body.Add(line.ToString());
    }

    public void AddHeader(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        _currentStep.Headers.Add(new KeyValueDescriptor(name, value));
    }

    public void AddExecutionInstruction(ReadOnlySpan<char> command, ReadOnlySpan<char> parameters)
    {
        if (command.Equals("clientsCount", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parameters, out int clientCount))
        {
            if (clientCount <= 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "ClientsCount must be a positive number"));
            _currentStep.ClientsCount = clientCount;
            return;
        }

        if (command.Equals("requestCount", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parameters, out int requestCount))
        {
            if (requestCount <= 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "ClientsCount must be a positive number"));
            _currentStep.RequestsCount = requestCount;
            return;
        }

        if (command.Equals("timeout", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parameters, out int timeout))
        {
            if (timeout <= 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "ClientsCount must be a positive number"));
            _currentStep.Timeout = TimeSpan.FromSeconds(timeout);
            return;
        }

        if (command.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            if (parameters.Length == 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "RequestName is empty"));
            var name = parameters.ToString();
            if (_steps.Any(x => name.Equals(x.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "RequestName already used"));
            _currentStep.Name = name;
            return;
        }

        if (command.Equals("sharedsocket", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(parameters, out var sharedSockets))
        {
            _currentStep.SharedSocket = sharedSockets;
            return;
        }
        if (command.Equals("no-redirects", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(parameters, out var noRedirects))
        {
            _currentStep.EnableRedirects = noRedirects;
            return;
        }
        if ((command.Equals("no-certificate-validation", StringComparison.OrdinalIgnoreCase)
            || command.Equals("no-cert-validation", StringComparison.OrdinalIgnoreCase))
            && bool.TryParse(parameters, out var noCertValidation))
        {
            _currentStep.NoCertificateValidation = noCertValidation;
            return;
        }
    }

    public void AddExecutionInstruction(ReadOnlySpan<char> command)
    {
        if (command.Equals("sharedsocket", StringComparison.OrdinalIgnoreCase))
        {
            _currentStep.SharedSocket = true;
            return;
        }
        if (command.Equals("no-redirects", StringComparison.OrdinalIgnoreCase))
        {
            _currentStep.EnableRedirects = false;
            return;
        }
        if (command.Equals("no-certificate-validation", StringComparison.OrdinalIgnoreCase)
            || command.Equals("no-cert-validation", StringComparison.OrdinalIgnoreCase))
        {
            _currentStep.NoCertificateValidation = true;
            return;
        }
    }

    public ExecutionPlan Build()
    {
        if (!_currentStep.IsDefault())
            _steps.Add(FreezeCurrentStep(_currentStep));
        return new ExecutionPlan(_steps, _variables);
    }

    private static FrozenExecutionStep FreezeCurrentStep(ExecutionStep step) =>
        new()
        {
            Method = step.Method ?? throw new ArgumentException(string.Format(null, ErrorFormat, step.LineNumber, step.NameOrUri(), "missing HTTP Verb")),
            Uri = step.Uri ?? throw new ArgumentException(string.Format(null, ErrorFormat, step.LineNumber, step.NameOrUri(), "missing Url")),
            Version = step.Version ?? HttpVersion.Version20,
            Name = step.Name,
            ClientsCount = step.ClientsCount,
            RequestsCount = step.RequestsCount,
            Timeout = step.Timeout,
            SharedSocket = step.SharedSocket,
            Body = step.Body,
            Headers = step.Headers,
        };
}
