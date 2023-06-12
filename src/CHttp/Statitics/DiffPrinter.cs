using System.Numerics;
using CHttp.Abstractions;
using CHttp.Data;

namespace CHttp.Statitics;

internal class DiffPrinter
{
    private readonly IConsole _console;

    internal DiffPrinter(IConsole console)
    {
        _console = console;
    }

    public void Compare(PerformanceMeasurementResults session0, PerformanceMeasurementResults session1)
    {
        if (session0.Summaries.Count == 0 || session0.Summaries.Count == 0)
        {
            _console.WriteLine("No measurements available");
            return;
        }
        var stats0 = Statistics.GetStats(session0);
        var stats1 = Statistics.GetStats(session1);

        _console.WriteLine($"RequestCount: {session0.Behavior.RequestCount}, Clients: {session0.Behavior.ClientsCount}");
        PrintLine("Mean:", Statistics.Display(stats0.Mean), Statistics.Display(stats1.Mean - stats0.Mean));
        PrintLine("StdDev:", Statistics.Display(stats0.StdDev), Statistics.Display(stats1.StdDev - stats0.StdDev));
        PrintLine("Error:", Statistics.Display(stats0.Error), Statistics.Display(stats1.Error - stats0.Error));
        PrintLine("Median:", Statistics.Display(stats0.Median), Statistics.Display(stats1.Median - stats0.Median));
        PrintLine("Min:", Statistics.Display(stats0.Min), Statistics.Display(stats1.Min - stats0.Min));
        PrintLine("Max:", Statistics.Display(stats0.Max), Statistics.Display(stats1.Max - stats0.Max));
        PrintLine("95th:", Statistics.Display(stats0.Percentile95th), Statistics.Display(stats1.Percentile95th - stats0.Percentile95th));
        PrintThroughput("Throughput:", SizeFormatter<double>.FormatSizeWithQualifier(stats0.Throughput), SizeFormatter<double>.FormatSizeWithQualifierWithSign(stats1.Throughput - stats0.Throughput));
        PrintRequestSec("Req/Sec:", stats0.RequestSec, stats1.RequestSec - stats0.RequestSec);

        int lineLength = _console.WindowWidth;
        var scaleNormalize = (double)lineLength / (stats0.Durations.Length + stats0.Durations.Length);
        string separator = new string('-', lineLength);

        // Histogram
        if (stats0.Durations.Length >= 100 && stats1.Durations.Length >= 100)
        {
            _console.WriteLine(separator);
            PrintHistogram(stats0, stats1, scaleNormalize);
        }
        _console.WriteLine(separator);

        PrintStatusCodes(stats0.StatusCodes, DiffStatusCodes(stats0, stats1));
        _console.WriteLine(separator);

        if (PrintWarnings(session0, session1))
            _console.WriteLine(separator);
    }

    private bool PrintWarnings(PerformanceMeasurementResults session0, PerformanceMeasurementResults session1)
    {
        bool writeSeparator = false;
        if (session0.Behavior != session1.Behavior)
        {
            var oldColor = _console.ForegroundColor;
            _console.ForegroundColor = ConsoleColor.DarkYellow;
            _console.WriteLine($"*Warning: session files use different test parameters: {session0.Behavior} and {session1.Behavior}");
            _console.ForegroundColor = oldColor;
            writeSeparator = true;
        }
        var distinctUrls = session0.Summaries.Union(session1.Summaries).Select(x => x.Url).Distinct().ToList();
        if (distinctUrls.Count > 1)
        {
            var oldColor = _console.ForegroundColor;
            _console.ForegroundColor = ConsoleColor.DarkYellow;
            _console.WriteLine($"*Warning: session files contain different urls: {string.Join(',', distinctUrls)}");
            _console.ForegroundColor = oldColor;
            writeSeparator = true;
        }
        return writeSeparator;
    }

    private void PrintLine(string name, (double DisplayValue, string Qualifier) baseValue, (double DisplayValue, string Qualifier) diff)
    {
        _console.Write($"| {name,-12}{baseValue.DisplayValue,10:F3} {baseValue.Qualifier}   ");
        var oldColor = _console.ForegroundColor;
        _console.ForegroundColor = diff.DisplayValue > 0 ? ConsoleColor.Red : ConsoleColor.Green;
        _console.Write($"{diff.DisplayValue,10:+#0.000;-#0.000;0} {diff.Qualifier}");
        _console.ForegroundColor = oldColor;
        _console.WriteLine("   |");
    }

    private void PrintThroughput(string name, (string DisplayValue, string Qualifier) baseValue, (string DisplayValue, string Qualifier) diff)
    {
        _console.WriteLine($"| {name,-12}{baseValue.DisplayValue,10} {baseValue.Qualifier}B/s {diff.DisplayValue,10} {diff.Qualifier}B/s |");
    }

    private void PrintRequestSec(string name, double baseValue, double diff)
    {
        _console.Write($"| {name,-12}{baseValue,10:G3}       ");
        var oldColor = _console.ForegroundColor;
        _console.ForegroundColor = diff >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
        _console.Write($"{diff,9:+#0.###;-#0.###;0}");
        _console.ForegroundColor = oldColor;
        _console.WriteLine("      |");
    }

    private static int[] DiffStatusCodes(Stats stats0, Stats stats1)
    {
        int[] diffStatusCodes = new int[stats0.StatusCodes.Length];
        for (int i = 0; i < diffStatusCodes.Length; i++)
        {
            diffStatusCodes[i] = stats1.StatusCodes[i] - stats0.StatusCodes[i];
        }
        return diffStatusCodes;
    }

    private void PrintStatusCodes(int[] statusCodes, int[] diff)
    {
        _console.WriteLine("HTTP status codes:");
        _console.WriteLine($"1xx: {statusCodes[0]} {diff[0]:+0;-0}, 2xx: {statusCodes[1]} {diff[1]:+0;-0}, 3xx: {statusCodes[2]} {diff[2]:+0;-0}, 4xx: {statusCodes[3]} {diff[3]:+0;-0}, 5xx: {statusCodes[4]} {diff[4]:+0;-0}, Other: {statusCodes[5]} {diff[5]:+0;-0}");
    }

    private void PrintHistogram(Stats stats0, Stats stats1, double scaleNormalize)
    {
        var sumStats = Stats.SumHistogram(stats0, stats1);
        (var bucketCount, var bSize) = Statistics.GetHistogramBuckets(sumStats);
        var bucketSize = new Vector<double>(bSize);

        var bucketLimit = new Vector<double>(sumStats.Min);
        var input0 = stats0.Durations.AsSpan();
        var input1 = stats1.Durations.AsSpan();
        for (int i = 0; i < bucketCount; i++)
        {
            bucketLimit += bucketSize;
            long currentCounter0 = GetCountForBucket(bucketLimit, ref input0);
            long currentCounter1 = GetCountForBucket(bucketLimit, ref input1);

            (var limit, var limitQualifier) = Statistics.Display(bucketLimit[0]);
            _console.Write($"{limit,10:F3} {limitQualifier} ");

            var counter0Count = (int)Math.Round(scaleNormalize * currentCounter0);
            var counter1Count = (int)Math.Round(scaleNormalize * currentCounter1);
            if (counter0Count > counter1Count)
            {
                _console.Write(new string('=', counter1Count));
                _console.Write(new string('#', counter0Count - counter1Count));
            }
            else if (counter0Count < counter1Count)
            {
                _console.Write(new string('=', counter0Count));
                _console.Write(new string('+', counter1Count - counter0Count));
            }
            else
            {
                _console.Write(new string('=', counter0Count));
            }
            _console.WriteLine();
        }
    }

    private static long GetCountForBucket(Vector<double> bucketLimit, ref Span<long> input)
    {
        long currentCounter = 0;
        var vSize = Vector<long>.Count;
        int oneCnt = vSize;
        while (oneCnt == vSize && input.Length >= vSize)
        {
            var vInput = Vector.ConvertToDouble(new Vector<long>(input));
            oneCnt = (int)Vector.Sum(Vector.LessThanOrEqual(vInput, bucketLimit)) * -1;
            currentCounter += oneCnt;
            input = input.Slice(oneCnt);
        }
        if (input.Length < vSize && input.Length > 0)
            currentCounter += input.Length;
        return currentCounter;
    }
}
