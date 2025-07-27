using System.CommandLine;
using System.Data;
using System.Globalization;
using System.Net;
using CHttp.Abstractions;
using CHttp.Binders;
using CHttp.Data;
using CHttp.Http;
using CHttp.Performance;
using CHttp.Performance.Statitics;
using CHttp.Writers;

namespace CHttp;

internal static class CommandFactory
{
    public static Command CreateRootCommand(
        IWriter? writer = null,
        IConsole? console = null,
        IFileSystem? fileSystem = null)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // kerberos auth
        // certificates
        // TLS
        // proxy

        var versionOptions = new Option<Version>(
            name: "--http-version")
        {
            DefaultValueFactory = _ => HttpVersion.Version30,
            Description = "The version of the HTTP request: 1.0, 1.1, 2, 3",
            Required = false,
            CustomParser = parseResult =>
            {
                var rawVersion = parseResult.Tokens.First().Value;
                return VersionParser.Map(rawVersion);
            }
        };
        versionOptions.Aliases.Add("-v");
        versionOptions.AcceptOnlyFromAmong(VersionParser.Version10, VersionParser.Version11, VersionParser.Version20, VersionParser.Version30);

        var methodOptions = new Option<HttpMethod>(
            name: "--method")
        {
            DefaultValueFactory = _ => HttpMethod.Get,
            Description = "HTTP Method",
            Required = false,
            CustomParser = parseResult =>
            {
                var rawMethod = parseResult.Tokens.First().Value;
                return new HttpMethod(rawMethod);
            },

        };
        methodOptions.Aliases.Add("-m");
        methodOptions.AcceptOnlyFromAmong(HttpMethod.Get.ToString(), HttpMethod.Put.ToString(), HttpMethod.Post.ToString(), HttpMethod.Delete.ToString(), HttpMethod.Head.ToString(), HttpMethod.Options.ToString(), HttpMethod.Trace.ToString(), HttpMethod.Trace.ToString(), HttpMethod.Connect.ToString());

        var headerOptions = new Option<IEnumerable<KeyValueDescriptor>>(
            name: "--header")
        {
            DefaultValueFactory = _ => [],
            Description = """Headers Key-Value pairs separated by ':'. For example --header="key:myvalue" """,
            Required = false,
            CustomParser = parseResult => [.. parseResult.Tokens.Select(token => new KeyValueDescriptor(token.Value))]
        };
        headerOptions.Aliases.Add("-h");
        headerOptions.Arity = ArgumentArity.ZeroOrMore;
        headerOptions.AllowMultipleArgumentsPerToken = true;

        var formsOptions = new Option<IEnumerable<KeyValueDescriptor>>(
            name: "--form")
        {
            DefaultValueFactory = _ => [],
            Description = """Forms Key-Value pairs separated by ':' For example --form="name:myvalue" """,
            Required = false,
            CustomParser = parseResult => [.. parseResult.Tokens.Select(token => new KeyValueDescriptor(token.Value))]
        };
        formsOptions.Aliases.Add("-f");
        formsOptions.Arity = ArgumentArity.ZeroOrMore;
        formsOptions.AllowMultipleArgumentsPerToken = true;

        var bodyOptions = new Option<string>(
            name: "--body")
        {
            Description = "Request body or a file path containing the request",
            Required = false,
        };
        bodyOptions.Aliases.Add("-b");

        var timeoutOption = new Option<double>(
            name: "--timeout")
        {
            DefaultValueFactory = _ => 30,
            Description = "Timeout in seconds."
        };
        timeoutOption.Aliases.Add("-t");

        var redirectOption = new Option<bool>(
            name: "--no-redirects")
        {
            DefaultValueFactory = _ => false,
            Description = "Disables following redirects on requests",
            CustomParser = parseResult =>
            {
                var value = parseResult.Tokens.FirstOrDefault()?.Value;
                if (value == null)
                    return false;
                if (bool.TryParse(value, out var result))
                    return !result; // Invert the value to match the option name
                parseResult.AddError("Invalid value for --no-redirects. Expected 'true' or 'false' or no value.");
                return false;
            }
        };

        var validateCertificateOption = new Option<bool>(
            name: "--no-certificate-validation")
        {
            DefaultValueFactory = _ => false,
            Description = "Disables certificate validation",
            CustomParser = parseResult =>
            {
                var value = parseResult.Tokens.FirstOrDefault()?.Value;
                if (value == null)
                    return false;
                if (bool.TryParse(value, out var result))
                    return !result; // Invert the value to match the option name
                parseResult.AddError("Invalid value for --no-certificate-validation. Expected 'true' or 'false' or no value.");
                return false;
            },

        };
        validateCertificateOption.Aliases.Add("--no-cert-validation");

        var uriOption = new Option<Uri>(
            name: "--uri")
        {
            Description = "The URL of the resource",
            Required = true,
            CustomParser = (parseResult) =>
            {
                var value = parseResult.Tokens.First().Value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uriResult))
                    parseResult.AddError($"Invalid URI: {value}");
                return uriResult;
            }
        };
        uriOption.Aliases.Add("-u");

        var cookieContainer = new Option<FileInfo?>(
            name: "--cookie-container")
        {
            Description = "A file to share cookies among requests.",
            Required = false,
        };

        var logOption = new Option<LogLevel>(
            name: "--log")
        {
            DefaultValueFactory = _ => LogLevel.Verbose,
            Description = "Level of logging details.",
            Required = false,
        };
        logOption.Aliases.Add("-l");
        logOption.AcceptOnlyFromAmong(nameof(LogLevel.Silent), nameof(LogLevel.Quiet), nameof(LogLevel.Normal), nameof(LogLevel.Verbose));

        var outputFileOption = new Option<FileInfo?>(
            name: "--output")
        {
            Description = "Output to file.",
            Required = false,
        };
        outputFileOption.Aliases.Add("-o");

        var cOption = new Option<int>(
            name: "--clients")
        {
            DefaultValueFactory = _ => 20,
            Description = "Number of parallel clients.",
            Required = false,
        };
        cOption.Aliases.Add("-c");

        var nOption = new Option<int>(
            name: "--requestCount")
        {
            DefaultValueFactory = _ => 100,
            Description = "Number of total requests sent.",
            Required = false,
        };
        nOption.Aliases.Add("-n");

        var uploadThrottleOption = new Option<int?>(
            name: "--upload-throttle")
        {
            DefaultValueFactory = _ => null,
            Description = "Specify HTTP level throttling in kbyte/sec when sending the request",
            Required = false,
        };

        var kerberosAuthOption = new Option<bool>(
            name: "--kerberos-auth")
        {
            DefaultValueFactory = _ => false,
            Description = "Use Kerberos Auth",
            Required = false,
        };
        kerberosAuthOption.Aliases.Add("-k");

        var shareSocketsHandlerOption = new Option<bool>(
            name: "--shared-sockethandler")
        {
            DefaultValueFactory = _ => false,
            Description = "Use pool sockets handler with allowng multiple connection",
            Required = false,
        };

        var diffFileOption = new Option<IEnumerable<FileInfo>>(
            name: "--files")
        {
            DefaultValueFactory = _ => [],
            Description = "List 2 files to be compared.",
            Required = false,
        };

        var metricsOption = new Option<string>(
            name: "--metrics")
        {
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("chttp_metrics", EnvironmentVariableTarget.Process) ?? string.Empty,
            Description = "Performance metrics data publihed to gRPC OpenTelemetry dashboards such as Aspire. Set format <endpoint;header>",
            Required = false,
        };

        var rootCommand = new RootCommand("Send HTTP request");

        // Only applies to the Root command.
        //rootCommand.Options.Add(bodyOptions);
        //rootCommand.Options.Add(uriOption);
        //rootCommand.Options.Add(uploadThrottleOption);
        //rootCommand.Options.Add(kerberosAuthOption);

        CreateFormsCommand(writer, fileSystem, versionOptions, methodOptions, headerOptions, formsOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, outputFileOption, cookieContainer, kerberosAuthOption, rootCommand);

        CreateDefaultCommand(writer, fileSystem, versionOptions, methodOptions, headerOptions, bodyOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, outputFileOption, cookieContainer, uploadThrottleOption, kerberosAuthOption, rootCommand);

        CreateMeasureCommand(console, fileSystem, versionOptions, methodOptions, headerOptions, bodyOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, nOption, cOption, outputFileOption, metricsOption, cookieContainer, kerberosAuthOption, shareSocketsHandlerOption, rootCommand);

        CreateDiffCommand(console, fileSystem, diffFileOption, rootCommand);

        return rootCommand;
    }

    private static void CreateFormsCommand(IWriter? writer,
        IFileSystem? fileSystem,
        Option<Version> versionOptions,
        Option<HttpMethod> methodOptions,
        Option<IEnumerable<KeyValueDescriptor>> headerOptions,
        Option<IEnumerable<KeyValueDescriptor>> formsOptions,
        Option<double> timeoutOption,
        Option<bool> redirectOption,
        Option<bool> validateCertificateOption,
        Option<Uri> uriOption,
        Option<LogLevel> logOption,
        Option<FileInfo?> outputFileOption,
        Option<FileInfo?> cookieContainerOption,
        Option<bool> kerberosAuthOption,
        RootCommand rootCommand)
    {
        var formsCommand = new Command("forms", "Forms request");

        // Shared options
        formsCommand.Options.Add(versionOptions);
        formsCommand.Options.Add(methodOptions);
        formsCommand.Options.Add(headerOptions);
        formsCommand.Options.Add(timeoutOption);
        formsCommand.Options.Add(redirectOption);
        formsCommand.Options.Add(validateCertificateOption);
        formsCommand.Options.Add(logOption);
        formsCommand.Options.Add(outputFileOption);
        formsCommand.Options.Add(cookieContainerOption);

        // Specific options
        formsCommand.Options.Add(formsOptions);
        formsCommand.Options.Add(uriOption);
        rootCommand.Add(formsCommand);
        formsCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var outputBehavior = new OutputBehaviorBinder(logOption, outputFileOption).Bind(parseResult);
            var httpBehavior = new HttpBehaviorBinder(redirectOption, validateCertificateOption, timeoutOption, cookieContainerOption, kerberosAuthOption).Bind(parseResult);
            writer ??= new WriterStrategy(outputBehavior);
            var cookieContainer = new PersistentCookieContainer(fileSystem ??= new FileSystem(), httpBehavior.CookieContainer);
            var client = new HttpMessageSender(writer, cookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);

            var forms = parseResult.GetRequiredValue(formsOptions);
            var formContent = new FormUrlEncodedContent(forms.Select(x => new KeyValuePair<string, string>(x.GetKey().ToString(), x.GetValue().ToString())));
            var requestDetails = new HttpRequestDetailsBinder(methodOptions, uriOption, versionOptions, headerOptions).Bind(parseResult, formContent);
            await client.SendRequestAsync(requestDetails, cancellationToken);
            await writer.CompleteAsync(CancellationToken.None);
            await cookieContainer.SaveAsync();
        });
    }

    private static void CreateDefaultCommand(IWriter? writer,
        IFileSystem? fileSystem,
        Option<Version> versionOptions,
        Option<HttpMethod> methodOptions,
        Option<IEnumerable<KeyValueDescriptor>> headerOptions,
        Option<string> bodyOptions,
        Option<double> timeoutOption,
        Option<bool> redirectOption,
        Option<bool> validateCertificateOption,
        Option<Uri> uriOption,
        Option<LogLevel> logOption,
        Option<FileInfo?> outputFileOption,
        Option<FileInfo?> cookieContainerOption,
        Option<int?> uploadThrottleOption,
        Option<bool> kerberosAuthOption,
        RootCommand rootCommand)
    {
        // Shared options
        rootCommand.Options.Add(versionOptions);
        rootCommand.Options.Add(methodOptions);
        rootCommand.Options.Add(headerOptions);
        rootCommand.Options.Add(timeoutOption);
        rootCommand.Options.Add(redirectOption);
        rootCommand.Options.Add(validateCertificateOption);
        rootCommand.Options.Add(logOption);
        rootCommand.Options.Add(outputFileOption);
        rootCommand.Options.Add(cookieContainerOption);

        // Specific options
        rootCommand.Options.Add(bodyOptions);
        rootCommand.Options.Add(uriOption);
        rootCommand.Options.Add(uploadThrottleOption);
        rootCommand.Options.Add(kerberosAuthOption);
        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var outputBehavior = new OutputBehaviorBinder(logOption, outputFileOption).Bind(parseResult);
            var httpBehavior = new HttpBehaviorBinder(redirectOption, validateCertificateOption, timeoutOption, cookieContainerOption, kerberosAuthOption).Bind(parseResult);
            var requestDetails = new HttpRequestDetailsBinder(methodOptions, uriOption, versionOptions, headerOptions).Bind(parseResult);
            var body = parseResult.GetValue(bodyOptions);
            var uploadThrottle = parseResult.GetValue(uploadThrottleOption);

            fileSystem ??= new FileSystem();
            writer ??= new WriterStrategy(outputBehavior);
            var cookieContainer = new PersistentCookieContainer(fileSystem, httpBehavior.CookieContainer);
            var client = new HttpMessageSender(writer, cookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
            if (uploadThrottle.HasValue && uploadThrottle.Value > 0)
                requestDetails = requestDetails with { Content = new UploadThrottledStringContent(body, uploadThrottle.Value, new Awaiter()) };
            else if (!string.IsNullOrEmpty(body))
                requestDetails = requestDetails with { Content = LoadRequestBody(fileSystem, body) };
            await client.SendRequestAsync(requestDetails, cancellationToken);
            await writer.CompleteAsync(cancellationToken);
            await cookieContainer.SaveAsync();
        });
    }

    private static void CreateMeasureCommand(IConsole? console,
        IFileSystem? fileSystem,
        Option<Version> versionOptions,
        Option<HttpMethod> methodOptions,
        Option<IEnumerable<KeyValueDescriptor>> headerOptions,
        Option<string> bodyOptions,
        Option<double> timeoutOption,
        Option<bool> redirectOption,
        Option<bool> validateCertificateOption,
        Option<Uri> uriOption,
        Option<int> nOption,
        Option<int> cOption,
        Option<FileInfo?> outputFileOption,
        Option<string> metricsOption,
        Option<FileInfo?> cookieContainerOption,
        Option<bool> kerberosAuthOption,
        Option<bool> shareSocketsHandlerOption,
        RootCommand rootCommand)
    {
        var perfCommand = new Command("perf", "Performance Measure");

        // Shared options
        perfCommand.Options.Add(versionOptions);
        perfCommand.Options.Add(methodOptions);
        perfCommand.Options.Add(headerOptions);
        perfCommand.Options.Add(timeoutOption);
        perfCommand.Options.Add(redirectOption);
        perfCommand.Options.Add(validateCertificateOption);
        perfCommand.Options.Add(outputFileOption);
        perfCommand.Options.Add(cookieContainerOption);
        perfCommand.Options.Add(kerberosAuthOption);

        // Specific options
        perfCommand.Options.Add(nOption);
        perfCommand.Options.Add(bodyOptions);
        perfCommand.Options.Add(cOption);
        perfCommand.Options.Add(uriOption);
        perfCommand.Options.Add(metricsOption);
        perfCommand.Options.Add(shareSocketsHandlerOption);
        rootCommand.Add(perfCommand);
        perfCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var httpBehavior = new HttpBehaviorBinder(redirectOption, validateCertificateOption, timeoutOption, cookieContainerOption, kerberosAuthOption).Bind(parseResult);
            var requestDetails = new HttpRequestDetailsBinder(methodOptions, uriOption, versionOptions, headerOptions).Bind(parseResult);
            var performanceBehavior = new PerformanceBehaviorBinder(nOption, cOption, shareSocketsHandlerOption).Bind(parseResult);
            var body = parseResult.GetValue(bodyOptions);
            var outputFile = parseResult.GetValue(outputFileOption)?.FullName ?? string.Empty;
            var metricsConnectionString = parseResult.GetValue(metricsOption);

            fileSystem ??= new FileSystem();
            if (!string.IsNullOrWhiteSpace(body))
                requestDetails = requestDetails with { Content = LoadRequestBody(fileSystem, body) };
            var socketsBehavior = httpBehavior.SocketsBehavior with { MaxConnectionPerServer = performanceBehavior.SharedSocketsHandler ? performanceBehavior.ClientsCount : 1 };
            httpBehavior = httpBehavior with { ToUtf8 = false, SocketsBehavior = socketsBehavior };
            console ??= new CHttpConsole();
            var cookieContainer = new PersistentCookieContainer(fileSystem, httpBehavior.CookieContainer);
            ISummaryPrinter printer = new StatisticsPrinter(console);
            if (!string.IsNullOrWhiteSpace(outputFile))
                printer = new CompositePrinter(printer, new FilePrinter(outputFile, fileSystem));
            if (!string.IsNullOrWhiteSpace(metricsConnectionString))
                printer = new CompositePrinter(printer, new OpenTelemtryPrinter(console, metricsConnectionString));
            BaseSocketsHandlerProvider socketsProvider = performanceBehavior.SharedSocketsHandler ? new SharedSocketsHandlerProvider() : new SingleSocketsHandlerProvider();
            var orchestrator = new PerformanceMeasureOrchestrator(printer, console, new Awaiter(), cookieContainer, socketsProvider, performanceBehavior);
            await orchestrator.RunAsync(requestDetails, httpBehavior, cancellationToken);
        });
    }

    private static void CreateDiffCommand(IConsole? console, IFileSystem? fileSystem, Option<IEnumerable<FileInfo>> diffFileOption, RootCommand rootCommand)
    {
        var diffCommand = new Command("diff", "Compares to performance measurement files");
        diffCommand.Options.Add(diffFileOption);
        rootCommand.Add(diffCommand);
        diffCommand.SetAction(async parseResult =>
        {
            var diffFiles = parseResult.GetRequiredValue<IEnumerable<FileInfo>>("--files");
            console ??= new CHttpConsole();
            fileSystem ??= new FileSystem();
            var filesCount = diffFiles.Count();
            if (filesCount == 0)
                return;
            if (filesCount == 1)
            {
                var session = await PerformanceFileHandler.LoadAsync(fileSystem, diffFiles.First().FullName);
                await new StatisticsPrinter(console).SummarizeResultsAsync(session);
                return;
            }
            if (filesCount > 1)
            {
                var session0 = await PerformanceFileHandler.LoadAsync(fileSystem, diffFiles.First().FullName);
                var session1 = await PerformanceFileHandler.LoadAsync(fileSystem, diffFiles.Last().FullName);
                var comparer = new DiffPrinter(console);
                comparer.Compare(session0, session1);
            }
        });
    }

    private static HttpContent LoadRequestBody(IFileSystem fileSystem, string input)
    {
        if (!fileSystem.Exists(input))
            return new StringContent(input);
        var contentStream = fileSystem.Open(input, FileMode.Open, FileAccess.Read);
        var memoryStream = new MemoryStream();
        contentStream.CopyTo(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        if (memoryStream.TryGetBuffer(out var segment))
            return new MemoryArrayContent(segment.AsMemory());
        return new MemoryArrayContent(memoryStream.ToArray());
    }
}
