public class Summary
{
    public string Error { get; set; }

    public override string ToString()
    {
        return Error;
    }
}
