using System.CommandLine;
using System.CommandLine.Binding;
using CHttp.Data;
using CHttp.Writers;

namespace CHttp.Binders;

internal sealed class OutputBehaviorBinder : BinderBase<OutputBehavior>
{
    private readonly Option<LogLevel> _logLevelOption;
    private readonly Option<string> _outputFileOption;

    public OutputBehaviorBinder(Option<LogLevel> logLevelOption, Option<string> outputFileOption)
    {
        _logLevelOption = logLevelOption;
        _outputFileOption = outputFileOption;
    }

    protected override OutputBehavior GetBoundValue(BindingContext bindingContext)
    {
        var logLevel = bindingContext.ParseResult.GetValueForOption(_logLevelOption);
        var outputFile = bindingContext.ParseResult.GetValueForOption(_outputFileOption) ?? string.Empty;

        return new OutputBehavior(logLevel, outputFile);
    }
}
