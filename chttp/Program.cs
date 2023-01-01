// See https://aka.ms/new-console-template for more information
using System.CommandLine;
using CHttp.Binders;

// kerberos auth
// certificates
// TLS
// proxy

var versionOptions = new Option<string?>(
    name: "--http-version",
    getDefaultValue: () => VersionBinder.Version30,
    description: "The version of the HTTP request: 1.0, 1.1, 2, 3");
versionOptions.AddAlias("-v");
versionOptions.IsRequired = false;
versionOptions.FromAmong(VersionBinder.Version10, VersionBinder.Version11, VersionBinder.Version20, VersionBinder.Version30);

var methodOptions = new Option<string?>(
    name: "--method",
    getDefaultValue: () => HttpMethod.Get.ToString(),
    description: "HTTP Method");
methodOptions.AddAlias("-m");
methodOptions.IsRequired = false;
methodOptions.FromAmong(HttpMethod.Get.ToString(), HttpMethod.Put.ToString(), HttpMethod.Post.ToString(), HttpMethod.Delete.ToString(), HttpMethod.Head.ToString(), HttpMethod.Options.ToString(), HttpMethod.Trace.ToString(), HttpMethod.Trace.ToString(), HttpMethod.Connect.ToString());

var headerOptions = new Option<IEnumerable<string>>(
    name: "--header",
    getDefaultValue: Enumerable.Empty<string>,
    description: "Headers Key-Value pairs separated by ':'");
headerOptions.AddAlias("-h");
headerOptions.IsRequired = false;
headerOptions.Arity = ArgumentArity.ZeroOrMore;
headerOptions.AllowMultipleArgumentsPerToken = true;

var formsOptions = new Option<IEnumerable<string>>(
    name: "--form",
    getDefaultValue: Enumerable.Empty<string>,
    description: "Forms Key-Value pairs separated by ':'");
formsOptions.AddAlias("-f");
formsOptions.IsRequired = false;
formsOptions.Arity = ArgumentArity.ZeroOrMore;
formsOptions.AllowMultipleArgumentsPerToken = true;

var bodyOptions = new Option<string>(
    name: "--body",
    description: "Request body");
bodyOptions.AddAlias("-b");
bodyOptions.IsRequired = true;

var timeoutOption = new Option<double>(
    name: "--timeout",
    getDefaultValue: () => 30,
    description: "Timeout in seconds.");
timeoutOption.AddAlias("-t");

var redirectOption = new Option<bool>(
    name: "--no-redirects",
    getDefaultValue: () => false,
    description: "Enables following redirects on requests");

var uriOption = new Option<string>(
    name: "--uri",
    description: "The URL of the resource");
uriOption.AddAlias("-u");
uriOption.IsRequired = true;

var rootCommand = new RootCommand("Send HTTP request");
rootCommand.AddGlobalOption(versionOptions);
rootCommand.AddGlobalOption(methodOptions);
rootCommand.AddGlobalOption(headerOptions);
rootCommand.AddGlobalOption(uriOption);
rootCommand.AddGlobalOption(timeoutOption);
rootCommand.AddGlobalOption(redirectOption);

var formsCommand = new Command("forms", "Forms request");
formsCommand.AddOption(formsOptions);
rootCommand.Add(formsCommand);
formsCommand.SetHandler(async (version, method, headers, uri, timeout, forms, redirects) =>
{
    var client = new HttpMessageSender(new ResponseWriter());
    var formContent = new FormUrlEncodedContent(forms.Select(x => new KeyValuePair<string, string>(x.GetKey().ToString(), x.GetValue().ToString())));
    await client.SendRequestAsync(new HttpRequestDetails(method, uri, version, headers, timeout) { Content = formContent }, new HttpBehavior(!redirects));
},
new VersionBinder(versionOptions),
new MethodBinder(methodOptions),
new KeyValueBinder(headerOptions),
new UriBinder(uriOption),
timeoutOption,
new KeyValueBinder(formsOptions),
new InvertBinder(redirectOption));

var jsonCommand = new Command("json", "Json request");
jsonCommand.AddOption(bodyOptions);
rootCommand.Add(jsonCommand);
jsonCommand.SetHandler(async (version, method, headers, uri, timeout, body, redirects) =>
{
    var client = new HttpMessageSender(new ResponseWriter());
    await client.SendRequestAsync(new HttpRequestDetails(method, uri, version, headers, timeout) { Content = new StringContent(body) }, new HttpBehavior(!redirects));
},
new VersionBinder(versionOptions),
new MethodBinder(methodOptions),
new KeyValueBinder(headerOptions),
new UriBinder(uriOption),
timeoutOption,
bodyOptions,
new InvertBinder(redirectOption));

rootCommand.SetHandler(async (version, method, headers, uri, timeout, redirects) =>
{
    var client = new HttpMessageSender(new ResponseWriter());
    await client.SendRequestAsync(new HttpRequestDetails(method, uri, version, headers, timeout), new HttpBehavior(redirects));
},
new VersionBinder(versionOptions),
new MethodBinder(methodOptions),
new KeyValueBinder(headerOptions),
new UriBinder(uriOption),
timeoutOption,
new InvertBinder(redirectOption));

await rootCommand.InvokeAsync(args);

public class Update
{

    public override string ToString()
    {
        return base.ToString();
    }
}


public class Summary
{

    public override string ToString()
    {
        return base.ToString();
    }
}

public class ConsoleWriter
{
    public virtual void WriteInfo(string info) => Console.WriteLine(info);

    public virtual void WriteUpdate(Update update) => Console.WriteLine(update.ToString());

    public virtual void WriteSummary(Summary summary) => Console.WriteLine(summary.ToString());
}

public class ResponseWriter : ConsoleWriter
{
    public override void WriteUpdate(Update update)
    {
        Console.WriteLine(update);
    }
}