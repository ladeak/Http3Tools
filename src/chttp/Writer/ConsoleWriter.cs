// See https://aka.ms/new-console-template for more information
public class ConsoleWriter : IWriter
{
    public virtual void WriteInfo(string info) => Console.WriteLine(info);

    public virtual void WriteUpdate(Update update) => Console.WriteLine(update.ToString());

    public virtual void WriteSummary(Summary summary) => Console.WriteLine(summary.ToString());

    public virtual void Write(ReadOnlySpan<char> info)
    {
        foreach (var c in info)
            Console.Write(c);
        Console.WriteLine();
    }
}
