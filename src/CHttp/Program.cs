using CHttp;

var commmand = CommandFactory.CreateRootCommand();
var parseResult = commmand.Parse(args);
foreach (var error in parseResult.Errors)
    Console.Error.WriteLine(error);
await parseResult.InvokeAsync();
