using System.Buffers;

namespace CHttpExecutor;

public static class VariablePreprocessor
{
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

    public static string Substitute(
        ReadOnlySpan<char> source,
        IReadOnlyDictionary<string, string> values)
    {
        var buffer = new PooledArrayBufferWriter<char>();
        bool runSubstitution = true;
        while (true)
        {
            runSubstitution = Substitute(source, values, buffer);
            if (!runSubstitution)
                return buffer.WrittenSpan.ToString();
            source = buffer.WrittenSpan.ToString();
            buffer.Clear();
        }
    }

    private static bool Substitute(
        ReadOnlySpan<char> source,
        IReadOnlyDictionary<string, string> values,
        PooledArrayBufferWriter<char> buffer)
    {
        bool hasReplaced = false;
        while (source.Length > 0)
        {
            var startIndex = source.IndexOf("{{");
            if (startIndex == -1)
            {
                buffer.Write(source);
                break;
            }

            buffer.Write(source[..startIndex]);

            var endIndex = source.Slice(startIndex).IndexOf("}}");
            if (endIndex == -1)
            {
                buffer.Write(source[startIndex..]);
                break;
            }

            // Remove ToString() call in .NET 9 https://github.com/dotnet/runtime/issues/27229
            var key = source.Slice(startIndex + 2, endIndex - 2).Trim().ToString();
            ReadOnlySpan<char> replacment = string.Empty;
            if (values.TryGetValue(key, out var value))
            {
                replacment = value;
                hasReplaced = true;
            }
            else
            {
                // No replacement
                replacment = source.Slice(startIndex, endIndex + 2);
            }
            if (replacment.StartsWith("{{$") && replacment.EndsWith("}}"))
            {
                var envVarName = replacment.Slice(3, replacment.Length - 5).Trim();
                replacment = Environment.GetEnvironmentVariable(envVarName.ToString()) ?? string.Empty;
                hasReplaced = true;
            }

            buffer.Write(replacment);
            source = source.Slice(startIndex + endIndex + 2);
        }
        return hasReplaced;
    }
}
