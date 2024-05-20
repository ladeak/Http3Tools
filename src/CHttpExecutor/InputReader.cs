using System.Text;
using CHttpExecutor;

public class InputReader(IExecutionPlanBuilder builder)
{
    private enum InputReaderState
    {
        Ready,
        Request,
        Headers,
        Body
    }

    public IEnumerable<string> Verbs = ["GET", "PUT", "POST", "DELETE", "HEAD", "OPTIONS", "TRACE", "PATCH"];

    private InputReaderState _state;

    public async Task<ExecutionPlan> ReadStreamAsync(Stream stream)
    {
        _state = InputReaderState.Ready;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line = await reader.ReadLineAsync();
        int lineNumber = 0;
        while (line != null)
        {
            ProcessLine(line, ++lineNumber);
            line = await reader.ReadLineAsync();
        }
        return builder.Build();
    }

    private void ProcessLine(string inputLine, int lineNumber)
    {
        ReadOnlySpan<char> line = inputLine.AsSpan().Trim();

        // New Step
        if (line.StartsWith("###"))
        {
            builder.AddStep(lineNumber);
            _state = InputReaderState.Ready;
            return;
        }

        // Empty line: separator
        if (line.Length == 0)
        {
            _state = _state switch
            {
                InputReaderState.Request => InputReaderState.Body,
                InputReaderState.Headers => InputReaderState.Body,
                _ => _state
            };
            return;
        }

        // Execution Instruction line
        if (line.StartsWith("# @") && _state == InputReaderState.Ready)
        {
            var instruction = line[3..];
            var separator = instruction.IndexOf(' ');
            if (separator > -1)
                builder.AddExecutionInstruction(instruction[..separator].Trim(), instruction[(separator + 1)..].Trim());
            else
                builder.AddExecutionInstruction(instruction.Trim());
            return;
        }

        // Comment line
        if (line.StartsWith("#"))
            return;

        // Variable definition
        if (line.StartsWith("@") && _state == InputReaderState.Ready)
        {
            var separtaor = line.IndexOf('=');
            if (separtaor == -1)
                ThrowArgumentException("Equal sign expected", lineNumber);
            builder.AddVariable(line[1..separtaor].Trim(), line[(separtaor + 1)..].Trim());
            return;
        }

        // Request line
        if (_state == InputReaderState.Ready)
        {
            string? requestVerb = null;
            foreach (string verb in Verbs)
            {
                if (line.Length > verb.Length + 1 &&
                    line.StartsWith(verb)
                    && line[verb.Length] == ' ')
                {
                    requestVerb = verb;
                    break;
                }
            }
            if (requestVerb != null)
            {
                _state = InputReaderState.Headers;
                builder.AddMethod(requestVerb);
                var endIndex = line.IndexOf(" HTTP/");
                if (endIndex > 0)
                {
                    builder.AddUri(line[requestVerb.Length..(endIndex)].Trim());
                    builder.AddHttpVersion(line[(endIndex + 6)..]);
                }
                else
                {
                    builder.AddUri(line[requestVerb.Length..].Trim());
                }
                return;
            }
            if (line.StartsWith("DIFF "))
                return;
            ThrowArgumentException("HTTP Verb expected.", lineNumber);
        }

        // Header
        if (_state == InputReaderState.Request || _state == InputReaderState.Headers)
        {
            _state = InputReaderState.Headers;
            var sepearator = line.IndexOf(':');
            if (sepearator == -1)
                ThrowArgumentException("Invalid Header format", lineNumber);
            builder.AddHeader(line[..sepearator].Trim(), line[(sepearator + 1)..].Trim());
            return;
        }

        // Body
        if (_state == InputReaderState.Body)
        {
            builder.AddBodyLine(line);
            return;
        }
    }

    public void ThrowArgumentException(string message, int lineNumber) =>
        throw new ArgumentException($"Invalid line {lineNumber}: {message}");
}
