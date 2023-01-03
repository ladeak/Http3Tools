public class ResponseWriter : ConsoleWriter
{
    public override void WriteUpdate(Update update)
    {
        Console.WriteLine(update);
    }
}