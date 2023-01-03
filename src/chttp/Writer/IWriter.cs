public interface IWriter
{
    void WriteInfo(string info);

    void Write(ReadOnlySpan<char> info);

    void WriteSummary(Summary summary);

    void WriteUpdate(Update update);
}