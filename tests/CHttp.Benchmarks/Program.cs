using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CHttpServer;
using CHttpServer.Http3;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

[DisassemblyDiagnoser]
public class Divison31
{
    [Params(62, 100, 124)]
    public int Input { get; set; }

    [Benchmark]
    public bool MultiplyBitShift() => (Input / 31) * 31 == Input;

    [Benchmark]
    public bool DivRem() => Input % 31 == 0;
}

[SimpleJob]
public class PrefixedIntegerDecoderBenchmarks
{
    /// <summary>
    /// 2147483647, 167321, 1433, 31, 1073741950
    /// </summary>
    public static byte[][] InputPrefixedSource { get; } = [
        [0b01111111, 0b10000000, 0b11111111, 0b11111111, 0b11111111, 0b00000111, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b01111111, 0b10011010, 0b10011010, 0b00001010, 0,          0         , 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b01111111, 0b10011010, 0b00001010, 0         , 0,          0         , 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b00011111, 0         , 0         , 0         , 0,          0         , 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b01111111, 0b11111111, 0b11111111, 0b11111111, 0b11111111, 0b00000011, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    ];

    [ParamsSource(nameof(InputPrefixedSource))]
    public static byte[] _inputPrefixed = [];

    [Benchmark]
    public int DecodeIntegerSimd()
    {
        QPackIntegerDecoder decoder = new();
        if (decoder.BeginTryDecode(_inputPrefixed[0], 7, out int result))
            return result;
        var index = 1;
        decoder.TryDecodeIntegerSimd(_inputPrefixed, ref index, out result);
        return result;
    }

    [Benchmark]
    public int DecodeInteger()
    {
        QPackIntegerDecoder decoder = new();
        if (decoder.BeginTryDecode(_inputPrefixed[0], 7, out int result))
            return result;
        var index = 1;
        decoder.TryDecodeInteger(_inputPrefixed, ref index, out result);
        return result;
    }
}

[SimpleJob]
public class VariableLengthIntegerDecoderBenchmarks
{
    /// <summary>
    /// 2147483647, 167321, 1433, 31, 1073741950
    /// </summary>
    public static byte[][] InputVariableSource { get; } = [
        [0b11000000, 0b00000000, 0b00000000, 0b00000000, 0b01111111, 0b11111111, 0b11111111, 0b11111111],
        [0b10000000, 0b00000010, 0b10001101, 0b10011001],
        [0b01000101, 0b10011001],
        [0b00011111],
        [0b11000000, 0b00000000, 0b00000000, 0b00000000, 0b01000000, 0b00000000, 0b00000000, 0b01111110]
    ];

    [ParamsSource(nameof(InputVariableSource))]
    public byte[] _inputVariable = [];

    [Benchmark]
    public int DecodeIntegerVariable()
    {
        VariableLenghtIntegerDecoder.TryRead(_inputVariable, out ulong result, out _);
        return (int)result;
    }
}

public class StdDevBenchmark
{
    public List<int> _numbers = new();

    public double _average;

    [GlobalSetup]
    public void Setup()
    {
        var numbers = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            numbers.Add(Random.Shared.Next(1, 100));
        }
        _numbers = numbers;
        _average = numbers.Average();
    }

    [Benchmark]
    public double Vectorized()
    {
        var avg = new Vector<double>(_average);
        var input = CollectionsMarshal.AsSpan(_numbers);
        double sum = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var vSize = Vector<int>.Count;
            while (input.Length >= vSize)
            {
                Vector.Widen(new Vector<int>(input), out var longLower, out var longUpper);

                var vInputLower = Vector.ConvertToDouble(longLower);
                var vInputUpper = Vector.ConvertToDouble(longUpper);
                var differenceLower = Vector.Subtract(avg, vInputLower);
                var differenceUpper = Vector.Subtract(avg, vInputUpper);
                var squaredLower = Vector.Multiply(differenceLower, differenceLower);
                var squaredUpper = Vector.Multiply(differenceUpper, differenceUpper);
                sum += Vector.Sum(squaredLower);
                sum += Vector.Sum(squaredUpper);
                input = input.Slice(vSize);
            }
        }

        // Remaining
        while (input.Length > 0)
        {
            var difference = _average - input[0];
            sum += difference * difference;
            input = input.Slice(1);
        }

        return sum / _numbers.Count;
    }

    [Benchmark]
    public double Linq()
    {
        return _numbers.Sum(x => Math.Pow(_average - x, 2)) / _numbers.Count;
    }
}

[SimpleJob, DisassemblyDiagnoser]
public class WriteUInt32
{
    public byte[] _buffer = new byte[3];

    [Params(123)]
    public uint Value { get; set; } = 300;

    [Benchmark]
    public void Shift() => WriteUInt24BigEndianShift(_buffer, Value);

    [Benchmark]
    public void Reverse() => WriteUInt24BigEndian(_buffer, Value);

    public static bool WriteUInt24BigEndian(Span<byte> destination, uint value)
    {
        if (destination.Length < 3 || value > (uint.MaxValue >> 8))
            return false;

        ref byte start = ref MemoryMarshal.GetReference(destination);
        Unsafe.WriteUnaligned(ref start, value);
        destination.Reverse();
        return true;
    }

    public static bool WriteUInt24BigEndianShift(Span<byte> destination, uint value)
    {
        if (destination.Length < 3 || value > 0xFF_FF_FF)
            return false;

        destination[2] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[0] = (byte)(value >> 16);
        return true;
    }

    public static uint ReadUInt24BigEndian(ReadOnlySpan<byte> source) => (uint)((source[0] << 16) | (source[1] << 8) | source[2]);
}

[SimpleJob, DisassemblyDiagnoser]
public class PrefixedIntegerEncoderBenchmarks
{
    //      0     3      4        5          6            7              8                9
    [Params(0, 1024, 17408, 2098176, 268436480, 34359739392, 4398046512128, 562949953427456)]
    public long Value { get; set; }

    [Benchmark]
    public bool EncodeLongSimd()
    {
        Span<byte> buffer = stackalloc byte[33];
        return QPackIntegerEncoder.TryEncodeSimd(buffer, Value, 7, out _);
    }

    [Benchmark]
    public bool EncodeLong()
    {
        Span<byte> buffer = stackalloc byte[33];
        return QPackIntegerEncoder.TryEncode(buffer, Value, 7, out _);
    }
}

[SimpleJob, DisassemblyDiagnoser]
public class Http3FramingStreamWriterBenchmarks
{
    private Http3FramingStreamWriter _writer = new Http3FramingStreamWriter(Stream.Null, 0);

    [Benchmark]
    public async Task FlushAsync()
    {
        Span<byte> data = [0, 1, 2, 3, 4];
        var memory0 = _writer.GetMemory(data.Length);
        data.CopyTo(memory0.Span);
        _writer.Advance(data.Length);

        var memory1 = _writer.GetMemory(data.Length);
        data.CopyTo(memory1.Span);
        _writer.Advance(data.Length);

        await _writer.FlushAsync();
    }
}

[SimpleJob, MemoryDiagnoser]
public class FeatureCollectionBenchmarks
{
    private interface F0 { }
    private interface F1 { }
    private interface F2 { }
    private interface F3 { }
    private interface F4 { }
    private interface F5 { }
    private interface F6 { }
    private interface F7 { }
    private interface F8 { }
    private interface F9 { }
    private interface F10 { }
    private interface F11 { }
    private interface F12 { }
    private interface F13 { }
    private interface F14 { }
    private interface F15 { }
    private interface F16 { }
    private interface F17 { }
    private interface F18 { }
    private interface F19 { }
    private class SampleFeature : F0, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19 { }

    private static SampleFeature Feature { get; } = new SampleFeature();

    [Benchmark]
    public bool FeatureCollectionLookup()
    {
        // Server
        var featureCollection = new FeatureCollectionLookup();
        featureCollection.Add<F0>(Feature);
        featureCollection.Add<F1>(Feature);

        // ToContextAware
        var contextFeatures = featureCollection.ToContextAware<object>();

        // Connection
        contextFeatures.Add<F3>(Feature);
        contextFeatures.Add<F4>(Feature);

        // Copy for stream
        var streamFeatures = contextFeatures.Copy();

        // Stream Add
        streamFeatures.Add<F5>(Feature);
        streamFeatures.Add<F6>(Feature);
        streamFeatures.Add<F7>(Feature);
        streamFeatures.Add<F8>(Feature);
        streamFeatures.Add<F9>(Feature);
        streamFeatures.Add<F10>(Feature);
        streamFeatures.Add<F11>(Feature);

        // Stream Checkpoint
        streamFeatures.Checkpoint();
        bool hasAll = true;
        for (int i = 0; i < 10; i++)
        {
            // Add more
            streamFeatures.Add<F12>(Feature);
            streamFeatures.Add<F13>(Feature);
            streamFeatures.Add<F14>(Feature);
            streamFeatures.Add<F15>(Feature);

            // Then retrieve
            hasAll &= streamFeatures.Get<F0>() != null;
            hasAll &= streamFeatures.Get<F1>() != null;
            hasAll &= streamFeatures.Get<F3>() != null;
            hasAll &= streamFeatures.Get<F4>() != null;
            hasAll &= streamFeatures.Get<F5>() != null;
            hasAll &= streamFeatures.Get<F6>() != null;
            hasAll &= streamFeatures.Get<F7>() != null;
            hasAll &= streamFeatures.Get<F8>() != null;
            hasAll &= streamFeatures.Get<F9>() != null;
            hasAll &= streamFeatures.Get<F10>() != null;
            hasAll &= streamFeatures.Get<F11>() != null;
            hasAll &= streamFeatures.Get<F12>() != null;
            hasAll &= streamFeatures.Get<F13>() != null;
            hasAll &= streamFeatures.Get<F14>() != null;
            hasAll &= streamFeatures.Get<F14>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;

            // Reset to checkpoint
            streamFeatures.ResetCheckpoint();
        }
        return hasAll;
    }

    [Benchmark]
    public bool FeatureCollection()
    {
        // Server
        var featureCollection = new FeatureCollection();
        featureCollection.AddRange(
            (typeof(F0), Feature),
            (typeof(F1), Feature));

        // ToContextAware
        var contextFeatures = featureCollection.ToContextAware<object>();

        // Connection
        contextFeatures.Add<F3>(Feature);
        contextFeatures.Add<F4>(Feature);

        // Copy for stream
        var streamFeatures = contextFeatures.Copy();

        // Stream Add
        streamFeatures.AddRange(
            (typeof(F5), Feature),
            (typeof(F6), Feature),
            (typeof(F7), Feature),
            (typeof(F8), Feature),
            (typeof(F9), Feature),
            (typeof(F10), Feature),
            (typeof(F11), Feature));

        // Stream Checkpoint
        streamFeatures.Checkpoint();
        bool hasAll = true;
        for (int i = 0; i < 10; i++)
        {
            // Add more
            streamFeatures.Add<F12>(Feature);
            streamFeatures.Add<F13>(Feature);
            streamFeatures.Add<F14>(Feature);
            streamFeatures.Add<F15>(Feature);

            // Then retrieve
            hasAll &= streamFeatures.Get<F0>() != null;
            hasAll &= streamFeatures.Get<F1>() != null;
            hasAll &= streamFeatures.Get<F3>() != null;
            hasAll &= streamFeatures.Get<F4>() != null;
            hasAll &= streamFeatures.Get<F5>() != null;
            hasAll &= streamFeatures.Get<F6>() != null;
            hasAll &= streamFeatures.Get<F7>() != null;
            hasAll &= streamFeatures.Get<F8>() != null;
            hasAll &= streamFeatures.Get<F9>() != null;
            hasAll &= streamFeatures.Get<F10>() != null;
            hasAll &= streamFeatures.Get<F11>() != null;
            hasAll &= streamFeatures.Get<F12>() != null;
            hasAll &= streamFeatures.Get<F13>() != null;
            hasAll &= streamFeatures.Get<F14>() != null;
            hasAll &= streamFeatures.Get<F14>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;
            hasAll &= streamFeatures.Get<F15>() != null;

            // Reset to checkpoint
            streamFeatures.ResetCheckpoint();
        }
        return hasAll;
    }
}