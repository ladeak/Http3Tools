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
        Substitute(source, values, buffer);
        return buffer.WrittenSpan.ToString();
    }

    private static void Substitute(
        ReadOnlySpan<char> source,
        IReadOnlyDictionary<string, string> values,
        PooledArrayBufferWriter<char> destination)
    {
        while (source.Length > 0)
        {
            var startIndex = source.IndexOf("{{");
            if (startIndex == -1)
            {
                destination.Write(source);
                return;
            }

            destination.Write(source[..startIndex]);

            var endIndex = source.Slice(startIndex).IndexOf("}}");
            if (endIndex == -1)
            {
                destination.Write(source[startIndex..]);
                return;
            }

            // Remove ToString() call in .NET 9 https://github.com/dotnet/runtime/issues/27229
            var key = source.Slice(startIndex + 2, endIndex - 2).Trim().ToString();
            if (!values.TryGetValue(key, out var value))
            {
                // No replacement
                destination.Write(source.Slice(startIndex, endIndex + 2));
            }
            else
            {
                var replacement = value.AsSpan();
                if (replacement.StartsWith("{{$") && replacement.EndsWith("}}"))
                {
                    var envVarName = replacement.Slice(3, replacement.Length - 5).Trim();
                    replacement = Environment.GetEnvironmentVariable(envVarName.ToString()) ?? string.Empty;
                }

                Substitute(replacement, values, destination);
            }

            source = source.Slice(startIndex + endIndex + 2);
        }
    }
}
