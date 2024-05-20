using System.Text.RegularExpressions;

namespace CHttpExecutor;

public partial class VariablePreprocessor
{
    [GeneratedRegex(@"{{\s*\w+\s*}}", RegexOptions.NonBacktracking)]
    private static partial Regex CaptureVariable();

    //public static string GetPlaceholderSubstitutable<T>(string input)
    //    where T : struct
    //{
    //    return CaptureVariable().Replace(input, default(T).ToString() ?? string.Empty);
    //}

    public static IReadOnlyCollection<string> GetVariableNames(ReadOnlySpan<char> source)
    {
        List<string> variableNames = new();
        while (source.Length > 0)
        {
            var startIndex = source.IndexOf("{{");
            if (startIndex == -1)
                break;
            var endIndex = source.Slice(startIndex).IndexOf("}}");
            if (endIndex == -1)
                break;
            variableNames.Add(source[(startIndex + 2)..endIndex].Trim().ToString());
            source = source.Slice(endIndex + 2);
        }
        return variableNames;
    }
}
