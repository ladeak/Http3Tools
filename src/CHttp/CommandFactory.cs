using System.CommandLine;
using CHttp.Binders;
using CHttp.Data;

namespace CHttp;

internal static class CommandFactory
{
    public static Command CreateRootCommand(IWriter? writer = null)
    {
        // kerberos auth
        // certificates
        // TLS
        // proxy

        var versionOptions = new Option<string>(
            name: "--http-version",
            getDefaultValue: () => VersionBinder.Version30,
            description: "The version of the HTTP request: 1.0, 1.1, 2, 3");
        versionOptions.AddAlias("-v");
        versionOptions.IsRequired = false;
        versionOptions.FromAmong(VersionBinder.Version10, VersionBinder.Version11, VersionBinder.Version20, VersionBinder.Version30);

        var methodOptions = new Option<string>(
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
            description: "Disables following redirects on requests");

        var validateCertificateOption = new Option<bool>(
            name: "--no-certificate-validation",
            getDefaultValue: () => false,
            description: "Disables certificate validation");
        validateCertificateOption.AddAlias("--no-cert-validation");

        var uriOption = new Option<string>(
            name: "--uri",
            description: "The URL of the resource");
        uriOption.AddAlias("-u");
        uriOption.IsRequired = true;

        var logOption = new Option<LogLevel>(
            name: "--log",
            getDefaultValue: () => LogLevel.Normal,
            description: "Level of logging details.");
        logOption.AddAlias("-l");
        logOption.IsRequired = false;
        logOption.FromAmong(nameof(LogLevel.Quiet), nameof(LogLevel.Normal), nameof(LogLevel.Verbose));

        var rootCommand = new RootCommand("Send HTTP request");
        rootCommand.AddGlobalOption(versionOptions);
        rootCommand.AddGlobalOption(methodOptions);
        rootCommand.AddGlobalOption(headerOptions);
        rootCommand.AddGlobalOption(uriOption);
        rootCommand.AddGlobalOption(timeoutOption);
        rootCommand.AddGlobalOption(redirectOption);
        rootCommand.AddGlobalOption(validateCertificateOption);
        rootCommand.AddGlobalOption(logOption);

        CreateFormsCommand(writer, versionOptions, methodOptions, headerOptions, formsOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, rootCommand);

        CreateJsonCommand(writer, versionOptions, methodOptions, headerOptions, bodyOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, rootCommand);

        CreateDefaultCommand(writer, versionOptions, methodOptions, headerOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, rootCommand);

        return rootCommand;
    }

    private static void CreateFormsCommand(IWriter? writer, Option<string> versionOptions, Option<string> methodOptions, Option<IEnumerable<string>> headerOptions, Option<IEnumerable<string>> formsOptions, Option<double> timeoutOption, Option<bool> redirectOption, Option<bool> validateCertificateOption, Option<string> uriOption, Option<LogLevel> logOption, RootCommand rootCommand)
    {
        var formsCommand = new Command("forms", "Forms request");
        formsCommand.AddOption(formsOptions);
        rootCommand.Add(formsCommand);
        formsCommand.SetHandler(async (requestDetails, httpBehavior, forms) =>
        {
            writer ??= new StatefulBufferedConsoleWriter();
            var client = new HttpMessageSender(new StatefulBufferedConsoleWriter());
            var formContent = new FormUrlEncodedContent(forms.Select(x => new KeyValuePair<string, string>(x.GetKey().ToString(), x.GetValue().ToString())));
            requestDetails = requestDetails with { Content = formContent };
            await client.SendRequestAsync(requestDetails, httpBehavior);
            await writer.CompleteAsync(CancellationToken.None);
        },
        new HttpRequestDetailsBinder(new HttpMethodBinder(methodOptions),
          new UriBinder(uriOption),
          new VersionBinder(versionOptions),
          new KeyValueBinder(headerOptions),
          timeoutOption),
        new HttpBehaviorBinder(
          new InvertBinder(redirectOption),
          new InvertBinder(validateCertificateOption),
        logOption),
        new KeyValueBinder(formsOptions));
    }

    private static void CreateDefaultCommand(IWriter? writer, Option<string> versionOptions, Option<string> methodOptions, Option<IEnumerable<string>> headerOptions, Option<double> timeoutOption, Option<bool> redirectOption, Option<bool> validateCertificateOption, Option<string> uriOption, Option<LogLevel> logOption, RootCommand rootCommand)
    {
        rootCommand.SetHandler(async (requestDetails, httpBehavior) =>
        {
            writer ??= new StatefulBufferedConsoleWriter();
            var client = new HttpMessageSender(writer);
            await client.SendRequestAsync(requestDetails, httpBehavior);
            await writer.CompleteAsync(CancellationToken.None);
        },
        new HttpRequestDetailsBinder(new HttpMethodBinder(methodOptions),
          new UriBinder(uriOption),
          new VersionBinder(versionOptions),
          new KeyValueBinder(headerOptions),
          timeoutOption),
        new HttpBehaviorBinder(
          new InvertBinder(redirectOption),
          new InvertBinder(validateCertificateOption),
        logOption));
    }

    private static void CreateJsonCommand(IWriter? writer, Option<string> versionOptions, Option<string> methodOptions, Option<IEnumerable<string>> headerOptions, Option<string> bodyOptions, Option<double> timeoutOption, Option<bool> redirectOption, Option<bool> validateCertificateOption, Option<string> uriOption, Option<LogLevel> logOption, RootCommand rootCommand)
    {
        writer ??= new StatefulBufferedConsoleWriter();
        var jsonCommand = new Command("json", "Json request");
        jsonCommand.AddOption(bodyOptions);
        rootCommand.Add(jsonCommand);
        jsonCommand.SetHandler(async (requestDetails, httpBehavior, body) =>
        {
            writer ??= new StatefulBufferedConsoleWriter();
            var client = new HttpMessageSender(writer);
            requestDetails = requestDetails with { Content = new StringContent(body) };
            await client.SendRequestAsync(requestDetails, httpBehavior);
            await writer.CompleteAsync(CancellationToken.None);
        },
        new HttpRequestDetailsBinder(new HttpMethodBinder(methodOptions),
          new UriBinder(uriOption),
          new VersionBinder(versionOptions),
          new KeyValueBinder(headerOptions),
          timeoutOption),
        new HttpBehaviorBinder(
          new InvertBinder(redirectOption),
          new InvertBinder(validateCertificateOption),
        logOption),
        bodyOptions);
    }
}
