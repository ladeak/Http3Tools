using CHttp;

var command = CommandFactory.CreateRootCommand();
var parseResult = command.Parse(args);
foreach (var error in parseResult.Errors)
    Console.Error.WriteLine(error);
await parseResult.InvokeAsync();
