using CHttp.Abstractions;
using CHttpExecutor;

var input = args[0];

var fileSytem = new FileSystem();
if (!fileSytem.Exists(input))
{
    Console.WriteLine($"{input} file does not exist");
    return;
}

try
{
    var fileStream = fileSytem.Open(input, FileMode.Open, FileAccess.Read);
    var reader = new InputReader(new ExecutionPlanBuilder());
    var plan = await reader.ReadStreamAsync(fileStream);
    var executor = new Executor(plan);
    await executor.ExecuteAsync();
}
catch (ArgumentException argEx)
{
    Console.WriteLine(argEx.Message);
}
