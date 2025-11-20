using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CHttpServer.Http3;

BenchmarkRunner.Run<IntegerDecoderBenchmarks>();

[SimpleJob, DisassemblyDiagnoser]
public class IntegerDecoderComparisonBenchmarks
{
    /// <summary>
    /// 2147483647, 167321, 1433, 31
    /// </summary>
    public static byte[][] _inputPrefixedSource = [
        [0b01111111, 0b10000000, 0b11111111, 0b11111111, 0b11111111, 0b00000111, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b01111111, 0b10011010, 0b10011010, 0b00001010, 0,          0         , 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b01111111, 0b10011010, 0b00001010, 0         , 0,          0         , 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0b00011111, 0         , 0         , 0         , 0,          0         , 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    ];

    public static byte[][] _inputVariableSource = [
        [0b11000000, 0b00000000, 0b00000000, 0b00000000, 0b01111111, 0b11111111, 0b11111111, 0b11111111],
        [0b10000000, 0b00000010, 0b10001101, 0b10011001],
        [0b01000101, 0b10011001],
        [0b00011111],
    ];

    [Params(0, 1, 2, 3)]
    public int InputIndex
    {
        get => field;
        set
        {
            field = value;
            _inputPrefixed = _inputPrefixedSource[value];
            _inputVariable = _inputVariableSource[value];
        }
    }

    public byte[] _inputPrefixed;

    public byte[] _inputVariable;

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
    public int DecodeIntegerVariable()
    {
        VariableLenghtIntegerDecoder.TryRead(_inputVariable, out ulong result, out _);
        return (int)result;
    }
}

[SimpleJob, DisassemblyDiagnoser]
public class IntegerDecoderBenchmarks
{
    public static byte[] _input = [0b0111_1111, 0b11111111, 0b11111111, 0b11111111, 0b11111111, 0b00000011, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Benchmark]
    public int DecodeInteger()
    {
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(_input[0], 7, out _);
        var index = 1;
        decoder.TryDecodeInteger(_input, ref index, out int result);
        return result;
    }

    [Benchmark]
    public int DecodeIntegerSimd()
    {
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(_input[0], 7, out _);
        var index = 1;
        decoder.TryDecodeIntegerSimd(_input, ref index, out int result);
        return result;
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