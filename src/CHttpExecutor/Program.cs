using CHttp.Abstractions;
using CHttpExecutor;

var input = args[0];

var fileSytem = new FileSystem();
var console = new CHttpConsole();
if (!fileSytem.Exists(input))
{
    console.WriteLine($"{input} file does not exist");
    return -1;
}

try
{
    var fileStream = fileSytem.Open(input, FileMode.Open, FileAccess.Read);
    var reader = new InputReader(new ExecutionPlanBuilder());
    var plan = await reader.ReadStreamAsync(fileStream);
    var executor = new Executor(plan, new CHttpConsole());
    if (!await executor.ExecuteAsync())
        return 1;
}
catch (ArgumentException argEx)
{
    console.WriteLine(argEx.Message);
    return -1;
}
return 0;