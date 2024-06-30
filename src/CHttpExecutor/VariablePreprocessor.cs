using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using CHttp.Writers;
using Json.More;
using Json.Path;

namespace CHttpExecutor;

internal static class VariablePreprocessor
{
    private static JsonSerializerOptions JsonOptions = new JsonSerializerOptions() { WriteIndented = false };

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

    public static bool TryGetReferencedRequestName(ReadOnlySpan<char> source, out ReadOnlySpan<char> requestName, out ReadOnlySpan<char> jsonPath)
    {
        requestName = default;
        jsonPath = default;
        var startIndex = source.IndexOf(".");
        if (startIndex == -1)
            return false;
        requestName = source.Slice(0, startIndex);
        jsonPath = source.Slice(startIndex + 1);
        return requestName.Length > 0 && jsonPath.Length > 0;
    }

    public static string Evaluate(
        ReadOnlySpan<char> source,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, VariablePostProcessingWriterStrategy> responses)
    {
        var buffer = new PooledArrayBufferWriter<char>();
        Evaluate(source, values, responses, buffer);
        return buffer.WrittenSpan.ToString();
    }

    private static void Evaluate(
        ReadOnlySpan<char> source,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, VariablePostProcessingWriterStrategy> responses,
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

            // TODO: Remove ToString() call in .NET 9 https://github.com/dotnet/runtime/issues/27229
            var key = source.Slice(startIndex + 2, endIndex - 2).Trim().ToString();

            // Default replacement is the textual form of the vairable.
            var replacement = source.Slice(startIndex, endIndex + 2);
            if (values.TryGetValue(key, out var value))
            {
                // Variable from values or env var
                replacement = value.AsSpan();
                if (replacement.StartsWith("{{$") && replacement.EndsWith("}}"))
                {
                    var envVarName = replacement.Slice(3, replacement.Length - 5).Trim();
                    replacement = Environment.GetEnvironmentVariable(envVarName.ToString()) ?? string.Empty;
                }

                Evaluate(replacement, values, responses, destination);
            }
            else if (TryGetReferencedRequestName(key, out var requestName, out var jsonPath)
                && responses.TryGetValue(requestName.ToString(), out var response)
                && TryGetPathValue(jsonPath, response, out var result))
            {
                // Variables from responses (no recursion)
                destination.Write(result);
            }
            else
            {
                // No replacement
                destination.Write(source.Slice(startIndex, endIndex + 2));
            }

            source = source.Slice(startIndex + endIndex + 2);
        }
    }

    private static bool TryGetPathValue(ReadOnlySpan<char> jsonPath, VariablePostProcessingWriterStrategy responseCtx, out string result)
    {
        const string responsePart = "response.";
        const string headersPart = "headers.";
        const string bodyPart = "body.";
        result = string.Empty;
        if (!jsonPath.StartsWith(responsePart, StringComparison.OrdinalIgnoreCase))
            return false;
        jsonPath = jsonPath.Slice(responsePart.Length);

        if (jsonPath.StartsWith(headersPart, StringComparison.OrdinalIgnoreCase))
            return ParseHeaderValue(jsonPath.Slice(headersPart.Length), responseCtx, ref result);

        if (jsonPath.StartsWith(bodyPart, StringComparison.OrdinalIgnoreCase))
            return ParseBody(jsonPath.Slice(bodyPart.Length - 1), responseCtx, ref result);

        return false;
    }

    private static bool ParseBodySimplified(ReadOnlySpan<char> jsonPath, VariablePostProcessingWriterStrategy responseCtx, ref string result)
    {
        // VSCE does not require $.
        if (jsonPath.StartsWith(".$"))
            jsonPath = jsonPath.Slice(2);

        responseCtx.Content.Seek(0, SeekOrigin.Begin);
        var jsonDoc = JsonDocument.Parse(responseCtx.Content);
        var currentElement = jsonDoc.RootElement;
        while (jsonPath.Length > 0)
        {
            var segment = jsonPath;
            var separatorIndex = jsonPath.Slice(1).IndexOfAny(".[");
            if (separatorIndex == -1)
            {
                segment = jsonPath;
                jsonPath = ReadOnlySpan<char>.Empty;
            }
            else
            {
                segment = jsonPath[..(separatorIndex + 1)];
                jsonPath = jsonPath.Slice(separatorIndex + 1);
            }

            if (currentElement.ValueKind == JsonValueKind.Object && currentElement.TryGetProperty(segment[1..], out var element))
            {
                currentElement = element;
            }
            else if (currentElement.ValueKind == JsonValueKind.Array
                && segment.Length > 2 && segment.StartsWith("[") && segment.EndsWith("]")
                && int.TryParse(segment[1..^1].Trim(), out var arrayIndex)
                && currentElement.GetArrayLength() > arrayIndex)
            {
                currentElement = currentElement.EnumerateArray().ElementAt(arrayIndex);
            }
            else
            {
                return false;
            }
        }
        result = currentElement.ToString();
        return true;
    }

    private static bool ParseBody(ReadOnlySpan<char> jsonPath, VariablePostProcessingWriterStrategy responseCtx, ref string result)
    {
        // VSCE does not require $.
        if (jsonPath.StartsWith(".$"))
            jsonPath = jsonPath.Slice(1);
        if (jsonPath.StartsWith("."))
            jsonPath = $"${jsonPath}";

        var path = JsonPath.Parse(jsonPath.ToString(), new PathParsingOptions() { AllowMathOperations = false, AllowRelativePathStart = false, AllowJsonConstructs = false, AllowInOperator = false, TolerateExtraWhitespace = true });
        responseCtx.Content.Seek(0, SeekOrigin.Begin);
        var instance = JsonNode.Parse(responseCtx.Content);
        var matches = path.Evaluate(instance);
        if (matches?.Matches == null || matches.Matches.Count == 0)
            return false;
        
        if (matches.Matches.Count != 1)
        {
            return false;
        }
        var matchedValue = matches.Matches.First().Value;
        if (matchedValue == null)
            return false;

        if (matchedValue.GetValueKind() == JsonValueKind.String)
            result = matchedValue.ToString();
        else
            result = matches.Matches.First().Value?.ToJsonString(JsonOptions) ?? string.Empty;
        return true;
    }

    private static bool ParseHeaderValue(ReadOnlySpan<char> jsonPath, VariablePostProcessingWriterStrategy responseCtx, ref string result)
    {
        var headerName = jsonPath.ToString();
        if (responseCtx.Headers?.TryGetValues(headerName, out var headerValues) ?? false)
        {
            result = HeaderWriter.WriteValue(headerName, headerValues);
            return true;
        }
        else if (responseCtx.ContentHeaders?.TryGetValues(jsonPath.ToString(), out headerValues) ?? false)
        {
            result = HeaderWriter.WriteValue(headerName, headerValues);
            return true;
        }
        else if (responseCtx.Trailers?.TryGetValues(jsonPath.ToString(), out headerValues) ?? false)
        {
            result = HeaderWriter.WriteValue(headerName, headerValues);
            return true;
        }
        return false;
    }
}
