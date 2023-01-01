// See https://aka.ms/new-console-template for more information
public interface IWriter
{
    void WriteInfo(string info);

    void Write(ReadOnlySpan<char> info);

    void WriteSummary(Summary summary);

    void WriteUpdate(Update update);
}