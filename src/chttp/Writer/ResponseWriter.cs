// See https://aka.ms/new-console-template for more information
public class ResponseWriter : ConsoleWriter
{
    public override void WriteUpdate(Update update)
    {
        Console.WriteLine(update);
    }
}