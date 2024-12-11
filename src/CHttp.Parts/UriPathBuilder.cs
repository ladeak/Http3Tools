using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace CHttp.Parts;

public static class UriPathBuilder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Create(UriPathInterpolatedStringHandler handler)
    {
        return handler.ToStringAndClear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Create(Span<char> initialBuffer, [InterpolatedStringHandlerArgument("initialBuffer")] UriPathInterpolatedStringHandler handler)
    {
        return handler.ToStringAndClear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string CreateCanonical(UriPathInterpolatedStringHandler handler)
    {
        return handler.ToCanonicalAndClear();
    }
}

[InterpolatedStringHandler]
public ref struct UriPathInterpolatedStringHandler
{
    private const char Slash = '/';
    private const string Ordinal = "ordinal";
    private const string Query = "query";
    private const int GuessedLengthPerHole = 11;
    private const int MinimumArrayPoolLength = 256;
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;
    private bool _isQuery = false;

    public UriPathInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _chars = _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
        _pos = 0;
    }

    public UriPathInterpolatedStringHandler(int literalLength, int formattedCount, Span<char> initialBuffer)
    {
        _chars = initialBuffer;
        _arrayToReturnToPool = null;
        _pos = 0;
    }

    public override string ToString() => new string(Text);

    public string ToStringAndClear()
    {
        string result = new string(Text);
        Clear();
        return result;
    }

    public string ToCanonicalAndClear()
    {
        const int schemaSeparatorLength = 3;
        var text = Text;
        var end = text.IndexOf('?');
        if (end < 0)
            end = _pos;

        int start;
        var schemaStart = text.IndexOf("://");
        if (schemaStart >= 0 && text.Length > schemaStart + schemaSeparatorLength)
        {
            schemaStart += schemaSeparatorLength;
            start = text[schemaStart..].IndexOf('/');
        }
        else
        {
            start = text.IndexOf('/');
            schemaStart = 0;
        }
        if (start >= 0)
        {
            start += schemaStart;
            var pathLength = PathNormalizer.RemoveDotSegments(_chars[start..end]);
            if (pathLength < end - start)
                _chars[end..].TryCopyTo(_chars[(start + pathLength)..]);
            _pos = start + pathLength + (_pos - end);
        }
        string result = new string(Text);
        Clear();
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Clear()
    {
        char[]? toReturn = _arrayToReturnToPool;
        //this = default; // defensive clear
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDefaultLength(int literalLength, int formattedCount) => int.Max(MinimumArrayPoolLength, literalLength + GuessedLengthPerHole * formattedCount);

    [UnscopedRef]
    private ReadOnlySpan<char> Text => _chars.Slice(0, _pos);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string value)
    {
        var source = value.AsSpan();

        // ROS overload
        AppendFormatted(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value)
    {
        AppendFormatted(value, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value, string? format)
    {
        string? s;
        if (format == Query)
        {
            _isQuery = true;
            format = Ordinal;
        }
        if (value is IFormattable)
        {
            if (value is ISpanFormattable spanFormattableValue)
            {
                // Expectation if the user omits the last slash: it would come from the formatted value.
                // However, most formatted values are the built-in types which do not emit a starting slash,
                // Hence add a slash and remove it later if the formatted value written a slash too.
                int charsWritten;
                if (format != Ordinal && !TryResolveSlashes())
                {
                    Grow();
                    var result = TryResolveSlashes();
                    Debug.Assert(result);
                }

                while (!spanFormattableValue.TryFormat(_chars.Slice(_pos), out charsWritten, format != Ordinal ? format : null, null))
                    Grow();

                // Remove duplicate slashes _pos points to the first char of the formatted value.
                // When it starts with a slash move the written segment one to the left.
                if (_pos > 0 && _chars[_pos] == Slash)
                {
                    _chars.Slice(_pos, charsWritten).CopyTo(_chars.Slice(_pos - 1));
                    _pos--;
                }

                _pos += charsWritten;
                return;
            }

            s = ((IFormattable)value).ToString(format: format, null);
        }
        else
        {
            s = value?.ToString();
        }

        if (s is not null)
        {
            if (format == Ordinal)
            {
                AppendFormattedOrdinal(s.AsSpan());
            }
            else
                AppendFormatted(s.AsSpan());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted(scoped ReadOnlySpan<char> value)
    {
        if (TryResolveSlashes(value) && value.TryCopyTo(_chars.Slice(_pos)))
            _pos += value.Length;
        else
            GrowAndAppend(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendFormattedOrdinal(scoped ReadOnlySpan<char> value)
    {
        if (value.TryCopyTo(_chars.Slice(_pos)))
            _pos += value.Length;
        else
            GrowAndAppend(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted(string? value)
    {
        if (value == null)
            return;
        AppendFormatted(value.AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted(object? value, string? format = null)
    {
        if (value == null)
            return;
        AppendFormatted<object>(value, format);
    }

    private void GrowAndAppend(scoped ReadOnlySpan<char> value)
    {
        var minRequired = value.Length + _pos + 1;
        Grow(minRequired);
        if (DoesRequireSlash(value))
            _chars[_pos++] = Slash;
        value.CopyTo(_chars.Slice(_pos));
        _pos += value.Length;
    }

    private void Grow()
    {
        var minRequired = _chars.Length * 2;
        Grow(minRequired);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow(int requiredMinCapacity)
    {
        var newCapacity = int.Max(requiredMinCapacity, _chars.Length * 2);
        int arraySize = Math.Clamp(newCapacity, MinimumArrayPoolLength, int.MaxValue);
        var newBuffer = ArrayPool<char>.Shared.Rent(arraySize);
        _chars[.._pos].CopyTo(newBuffer);
        char[]? toReturn = _arrayToReturnToPool;
        _chars = newBuffer;
        _arrayToReturnToPool = newBuffer;
        if (toReturn is not null)
            ArrayPool<char>.Shared.Return(_arrayToReturnToPool);

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool DoesRequireSlash(scoped ReadOnlySpan<char> value)
    {
        return _pos > 0 && _chars[_pos - 1] != Slash && value[0] != Slash && !_isQuery;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveSlashes(scoped ReadOnlySpan<char> value)
    {
        // Ignore slashes at the beginning of string.
        if (_pos <= 0 || _isQuery)
            return true;

        var lastSlash = _chars[_pos - 1] == Slash;
        var firstSlash = value[0] == Slash;
        if (firstSlash && lastSlash)
            _pos = int.Max(0, _pos - 1);
        else if (!firstSlash && !lastSlash)
        {
            if (_chars.Length <= _pos + 1)
                return false;
            _chars[_pos++] = Slash;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveSlashes()
    {
        // Ignore slashes at the beginning of string.
        if (_pos <= 0 || _isQuery)
            return true;

        // Expectation if the user omits the last slash: it would come from the formatted value.
        // However, most formatted values are the built-in types which do not emit a starting slash,
        // Hence add a slash and remove it later if the formatted value written a slash too.
        var lastSlash = _chars[_pos - 1] == Slash;
        if (lastSlash)
            return true;

        if (_chars.Length <= _pos + 1)
            return false;

        _chars[_pos++] = Slash;
        return true;
    }
}

internal static class PathNormalizer
{
    private const char ByteSlash = '/';
    private const char ByteDot = '.';

    // In-place implementation of the algorithm from https://tools.ietf.org/html/rfc3986#section-5.2.4
    public static int RemoveDotSegments(Span<char> src)
    {
        Debug.Assert(src[0] == '/', "Path segment must always start with a '/'");
        ReadOnlySpan<char> slashDot = "/.";
        Vector<ushort> vSlash = new Vector<ushort>(ByteSlash);
        Vector<ushort> vDot = new Vector<ushort>(ByteDot);
        var srcVector = MemoryMarshal.Cast<char, ushort>(src);

        var writtenLength = 0;
        var readPointer = 0;

        while (src.Length > readPointer)
        {
            if (Vector256.IsHardwareAccelerated)
            {
                while (srcVector.Length > readPointer + Vector<ushort>.Count + 1)
                {
                    var vInput = Vector.LoadUnsafe(ref srcVector[readPointer]);
                    var vMatchSlash = Vector.Equals(vInput, vSlash);
                    var vInput1 = Vector.LoadUnsafe(ref srcVector[readPointer + 1]);
                    var vMatchDot = Vector.Equals(vInput1, vDot);
                    if ((vMatchSlash & vMatchDot) == Vector<ushort>.Zero)
                    {
                        if (readPointer != writtenLength)
                        {
                            vInput.StoreUnsafe(ref srcVector[writtenLength]);
                        }
                        writtenLength += Vector<ushort>.Count;
                        readPointer += Vector<ushort>.Count;
                    }
                    else
                    {
                        uint mask = (vMatchSlash & vMatchDot).AsVector256().ExtractMostSignificantBits();
                        while (mask > 0)
                        {
                            var index = BitOperations.TrailingZeroCount(mask);
                            if (readPointer != writtenLength)
                                srcVector.Slice(readPointer, index).CopyTo(srcVector[writtenLength..]);
                            writtenLength += index;
                            readPointer += index;

                            var rpBefore = readPointer;
                            if (HandleSlashDotBegin(src, ref writtenLength, ref readPointer))
                            {
                                return writtenLength;
                            }
                            mask >>= int.Min(31, index + readPointer - rpBefore);
                        }
                    }
                    break;
                }
            }

            var currentSrc = src[readPointer..];
            var nextDotSegmentIndex = currentSrc.IndexOf(slashDot);
            if (nextDotSegmentIndex < 0 && readPointer == 0)
            {
                return src.Length;
            }
            if (nextDotSegmentIndex < 0)
            {
                // Copy the remianing src to dst, and return.
                src[readPointer..].CopyTo(src[writtenLength..]);
                writtenLength += src.Length - readPointer;
                return writtenLength;
            }
            else if (nextDotSegmentIndex > 0)
            {
                // Copy until the next segment excluding the trailer.
                currentSrc[..nextDotSegmentIndex].CopyTo(src[writtenLength..]);
                writtenLength += nextDotSegmentIndex;
                readPointer += nextDotSegmentIndex;
            }

            if (HandleSlashDotBegin(src, ref writtenLength, ref readPointer))
            {
                return writtenLength;
            }
        }
        return writtenLength;
    }

    private static bool HandleSlashDotBegin(Span<char> src, ref int writtenLength, ref int readPointer)
    {
        ReadOnlySpan<char> dotSlash = "./";
        ReadOnlySpan<char> slashDot = "/.";

        // Case of /../ or /./
        int remainingLength = src.Length - readPointer;
        if (remainingLength > 3)
        {
            var nextIndex = readPointer + 2;
            if (src[nextIndex] == ByteSlash)
            {
                readPointer = nextIndex;
            }
            else if (MemoryMarshal.CreateSpan(ref src[nextIndex], 2).StartsWith(dotSlash))
            {
                // Remove the last segment and replace the path with /
                var lastIndex = MemoryMarshal.CreateSpan(ref src[0], writtenLength).LastIndexOf(ByteSlash);

                // Move write pointer to the end of the previous segment without / or to start position
                writtenLength = int.Max(0, lastIndex);

                // Move the read pointer to the next segments beginning including /
                readPointer += 3;
            }
            else
            {
                // No dot segment, copy the matched /. and the next character and bump the read pointer
                src.Slice(readPointer, 3).CopyTo(src[writtenLength..]);
                writtenLength += 3;
                readPointer = nextIndex + 1;
            }
            return false;
        }
        // Ending with /.. or /./
        else if (remainingLength == 3)
        {
            var nextIndex = readPointer + 2;
            if (src[nextIndex] == ByteSlash)
            {
                // Replace the /./ segment with a closing /
                src[writtenLength++] = ByteSlash;
                return true;
            }
            else if (src[nextIndex] == ByteDot)
            {
                // Remove the last segment and replace the path with /
                var lastSlashIndex = MemoryMarshal.CreateSpan(ref src[0], writtenLength).LastIndexOf(ByteSlash);

                // If this was the beginning of the string, then return /
                if (lastSlashIndex < 0)
                {
                    src[0] = ByteSlash;
                    writtenLength = 1;
                    return true;
                }
                else
                {
                    writtenLength = lastSlashIndex + 1;
                }
                return true;
            }
            else
            {
                // No dot segment, copy the /. and the last character.
                src[readPointer..].CopyTo(src[writtenLength..]);
                writtenLength += 3;
                return true;
            }
        }
        // Ending with /.
        else if (remainingLength == 2)
        {
            src[writtenLength++] = ByteSlash;
            return true;
        }
        return false;
    }
}