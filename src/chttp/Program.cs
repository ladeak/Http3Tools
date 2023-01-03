using System.CommandLine;
using CHttp;

var commmand = CommandFactory.CreateRootCommand();
await commmand.InvokeAsync(args);
