using System.CommandLine;
using CHttp.Data;
using CHttp.Writers;

namespace CHttp.Binders;

internal sealed class OutputBehaviorBinder
{
    private readonly Option<LogLevel> _logLevelOption;
    private readonly Option<FileInfo?> _outputFileOption;

    public OutputBehaviorBinder(Option<LogLevel> logLevelOption, Option<FileInfo?> outputFileOption)
    {
        _logLevelOption = logLevelOption;
        _outputFileOption = outputFileOption;
    }

    internal OutputBehavior Bind(ParseResult parseResult)
    {
        var logLevel = parseResult.GetValue(_logLevelOption);
        var outputFile = parseResult.GetValue(_outputFileOption)?.Name ?? string.Empty;

        return new OutputBehavior(logLevel, outputFile);
    }
}
