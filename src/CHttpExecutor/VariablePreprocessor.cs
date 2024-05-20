using System.Text.RegularExpressions;

namespace CHttpExecutor;

public partial class VariablePreprocessor
{
    [GeneratedRegex(@"{{\s*\w+\s*}}", RegexOptions.NonBacktracking)]
    private partial Regex CaptureVariable();
}
