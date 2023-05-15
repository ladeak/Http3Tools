using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

var b = new StdDevBenchmark();
b.Setup();
var vectorizedResult = b.Vectorized();
var linqResult = b.Linq();
Console.WriteLine(linqResult);
Console.WriteLine(vectorizedResult);
Console.WriteLine(linqResult - vectorizedResult < double.Epsilon);

BenchmarkRunner.Run<StdDevBenchmark>();

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