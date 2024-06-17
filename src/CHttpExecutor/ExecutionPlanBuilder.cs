using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using CHttp.Data;

namespace CHttpExecutor;

internal interface IExecutionPlanBuilder
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

internal partial class ExecutionPlanBuilder : IExecutionPlanBuilder
{
    private static CompositeFormat ErrorFormat = CompositeFormat.Parse("Line {0}, with {1} {2}");

    private List<FrozenExecutionStep> _steps = new();

    private HashSet<string> _variables = new();

    private ExecutionStep _currentStep = new() { LineNumber = 1 };

    public void AddStep(int lineNumber)
    {
        // If variables defined 
        var variables = new List<Variable>();
        if (_currentStep.IsOnlyRollingParameter)
            _currentStep = new() { LineNumber = lineNumber, Variables = _currentStep.Variables };
        else
        {
            if (!_currentStep.IsDefault)
                _steps.Add(FreezeCurrentStep(_currentStep));
            _currentStep = new() { LineNumber = lineNumber };
        }
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
        var key = name.ToString();
        ValidateVariableExistance(value);
        var variable = new Variable(key, value.ToString());
        _variables.Add(key);
        _currentStep.Variables.Add(variable);
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
            && VarValue<int>.TryCreate(parameters, out var clientCount))
        {
            if (clientCount.HasValue && clientCount.Value <= 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "ClientsCount must be a positive number"));
            ValidateVariableExistance(clientCount);
            _currentStep.ClientsCount = clientCount;
            return;
        }

        if (command.Equals("requestCount", StringComparison.OrdinalIgnoreCase)
            && VarValue<int>.TryCreate(parameters, out var requestCount))
        {
            if (requestCount.HasValue && requestCount.Value <= 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "RequestCount must be a positive number"));
            ValidateVariableExistance(requestCount);
            _currentStep.RequestsCount = requestCount;
            return;
        }

        if (command.Equals("timeout", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parameters, out int timeout))
            {
                if (timeout <= 0)
                    throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "ClientsCount must be a positive number"));
                _currentStep.Timeout = new VarValue<double>(timeout);
            }
            else
            {
                var timeoutExpression = new VarValue<double>(parameters.ToString());
                ValidateVariableExistance(timeoutExpression);
                _currentStep.Timeout = timeoutExpression;
            }
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
            && VarValue<bool>.TryCreate(parameters, out var sharedSockets))
        {
            ValidateVariableExistance(sharedSockets);
            _currentStep.SharedSocket = sharedSockets;
            return;
        }

        if (command.Equals("no-redirects", StringComparison.OrdinalIgnoreCase)
            && VarValue<bool>.TryCreate(parameters, out var noRedirects))
        {
            ValidateVariableExistance(noRedirects);
            _currentStep.EnableRedirects = noRedirects;
            return;
        }

        if ((command.Equals("no-certificate-validation", StringComparison.OrdinalIgnoreCase)
            || command.Equals("no-cert-validation", StringComparison.OrdinalIgnoreCase))
            && VarValue<bool>.TryCreate(parameters, out var noCertValidation))
        {
            ValidateVariableExistance(noCertValidation);
            _currentStep.NoCertificateValidation = noCertValidation;
            return;
        }

        if (command.Equals("assert", StringComparison.OrdinalIgnoreCase)
            || command.Equals("assertion", StringComparison.OrdinalIgnoreCase))
        {
            if (parameters.Length == 0)
                throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, "Assertion is empty"));
            while (parameters.Length > 0)
            {
                _currentStep.Assertions.Add(ParseAssert(ref parameters));
            }
            return;
        }
    }

    public void AddExecutionInstruction(ReadOnlySpan<char> command)
    {
        if (command.Equals("sharedsocket", StringComparison.OrdinalIgnoreCase))
        {
            _currentStep.SharedSocket = VarValue.True;
            return;
        }
        if (command.Equals("no-redirects", StringComparison.OrdinalIgnoreCase))
        {
            _currentStep.EnableRedirects = VarValue.False;
            return;
        }
        if (command.Equals("no-certificate-validation", StringComparison.OrdinalIgnoreCase)
            || command.Equals("no-cert-validation", StringComparison.OrdinalIgnoreCase))
        {
            _currentStep.NoCertificateValidation = VarValue.True;
            return;
        }
    }

    public ExecutionPlan Build()
    {
        if (!_currentStep.IsDefault)
            _steps.Add(FreezeCurrentStep(_currentStep));
        return new ExecutionPlan(_steps, _variables);
    }

    private static FrozenExecutionStep FreezeCurrentStep(ExecutionStep step) =>
        new()
        {
            LineNumber = step.LineNumber,
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
            EnableRedirects = step.EnableRedirects,
            NoCertificateValidation = step.NoCertificateValidation,
            Variables = step.Variables,
            Assertions = step.Assertions
        };

    private void ValidateVariableExistance<T>(VarValue<T> source)
        where T : ISpanParsable<T>
    {
        if (source.HasValue)
            return;
        ValidateVariableExistance(source.VariableValue);
    }

    private void ValidateVariableExistance(ReadOnlySpan<char> source)
    {
        // Can be a named variable or a step name
        var undefinedVar = VariablePreprocessor.GetVariableNames(source)
            .Where(x => !_variables.Contains(x))
            .Where(x => !_steps.Any(s => s.Name != null && x.StartsWith(s.Name)))
            .Where(x => !(_currentStep.Name != null && x.StartsWith(_currentStep.Name))).FirstOrDefault();

        if (undefinedVar != null)
            throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, $"{undefinedVar} is not yet defined"));
    }

    private Assertion ParseAssert(ref ReadOnlySpan<char> parameters)
    {
        if (parameters.StartsWith("mean", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(4);
            var parsed = ParseAssert<double>(true, ref parameters, _currentStep);
            return new MeanAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("median", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(6);
            var parsed = ParseAssert<long>(true, ref parameters, _currentStep);
            return new MedianAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("stddev", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(6);
            var parsed = ParseAssert<double>(true, ref parameters, _currentStep);
            return new StdDevAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("error", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(5);
            var parsed = ParseAssert<double>(true, ref parameters, _currentStep);
            return new ErrorAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("requestsec", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(10);
            var parsed = ParseAssert<double>(false, ref parameters, _currentStep);
            return new RequestSecAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("throughput", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(10);
            var parsed = ParseAssert<double>(false, ref parameters, _currentStep);
            return new ThroughputAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("min", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(3);
            var parsed = ParseAssert<long>(true, ref parameters, _currentStep);
            return new MinAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("max", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(3);
            var parsed = ParseAssert<long>(true, ref parameters, _currentStep);
            return new MaxAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("percentile95th", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(14);
            var parsed = ParseAssert<long>(true, ref parameters, _currentStep);
            return new Percentile95thAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        if (parameters.StartsWith("successStatus", StringComparison.OrdinalIgnoreCase))
        {
            parameters = parameters.Slice(13);
            var parsed = ParseAssert<long>(false, ref parameters, _currentStep);
            return new SuccessStatusCodesAssertion() { Comperand = parsed.Value, Comperator = parsed.Operator };
        }
        throw new ArgumentException(string.Format(null, ErrorFormat, _currentStep.LineNumber, _currentStep.Name, $"Invalid assertion parameter: {parameters}"));
    }

    protected static (ComparingOperation Operator, TParsed Value) ParseAssert<TParsed>(bool timebased, ref ReadOnlySpan<char> parameters, ExecutionStep step) where TParsed : INumber<TParsed>
    {
        parameters = parameters.TrimStart();
        ComparingOperation comparator;
        if (parameters.StartsWith("=="))
        {
            comparator = ComparingOperation.Equals;
            parameters = parameters.Slice(2);
        }
        else if (parameters.StartsWith("!="))
        {
            comparator = ComparingOperation.NotEquals;
            parameters = parameters.Slice(2);
        }
        else if (parameters.StartsWith("<="))
        {
            comparator = ComparingOperation.LessThenOrEquals;
            parameters = parameters.Slice(2);
        }
        else if (parameters.StartsWith(">="))
        {
            comparator = ComparingOperation.MoreThenOrEquals;
            parameters = parameters.Slice(2);
        }
        else if (parameters.StartsWith("<"))
        {
            comparator = ComparingOperation.LessThen;
            parameters = parameters.Slice(1);
        }
        else if (parameters.StartsWith(">"))
        {
            comparator = ComparingOperation.MoreThen;
            parameters = parameters.Slice(1);
        }
        else
            throw new ArgumentException(string.Format(null, ErrorFormat, step.LineNumber, step.Name, $"Invalid assertion comparator: {parameters}"));

        parameters = parameters.TrimStart();
        var nextSeparator = parameters.IndexOfAny(" \t");
        if (nextSeparator == -1)
            nextSeparator = parameters.Length;
        var rawValue = parameters.Slice(0, nextSeparator);

        // Parse double value with quantifier
        if (!TryParseQuantifiedValue<TParsed>(rawValue, timebased ? TimeSpan.TicksPerSecond : 1, out TParsed? comperand))
            if (!TParsed.TryParse(rawValue, CultureInfo.InvariantCulture, out comperand))
                throw new ArgumentException(string.Format(null, ErrorFormat, step.LineNumber, step.Name, $"Invalid assertion comparison value: {rawValue}"));
        parameters = parameters.Slice(nextSeparator).Trim();
        return (comparator, comperand);
    }

    private static bool TryParseQuantifiedValue<TParsed>(ReadOnlySpan<char> parameter, double multiplier, [NotNullWhen(true)] out TParsed? result) where TParsed : INumber<TParsed>
    {
        parameter = parameter.TrimEnd();
        if (parameter.Length > 2 && parameter[^2] == 'n' && parameter[^1] == 's')
        {
            multiplier = 1.0 / TimeSpan.NanosecondsPerTick;
            parameter = parameter[0..^2];
        }
        else if (parameter.Length > 2 && parameter[^2] == 'u' && parameter[^1] == 's')
        {
            multiplier = TimeSpan.TicksPerMicrosecond;
            parameter = parameter[0..^2];
        }
        else if (parameter.Length > 2 && parameter[^2] == 'm' && parameter[^1] == 's')
        {
            multiplier = TimeSpan.TicksPerMillisecond;
            parameter = parameter[0..^2];
        }
        else if (parameter.Length > 1 && parameter[^1] == 's')
        {
            multiplier = TimeSpan.TicksPerSecond;
            parameter = parameter[0..^1];
        }
        else if (parameter.Length > 1 && parameter[^1] == 'm')
        {
            multiplier = TimeSpan.TicksPerMinute;
            parameter = parameter[0..^1];
        }

        if (!double.TryParse(parameter, CultureInfo.InvariantCulture, out double comperand))
        {
            result = default;
            return false;
        }

        result = TParsed.CreateChecked(comperand * multiplier);
        return true;
    }
}
