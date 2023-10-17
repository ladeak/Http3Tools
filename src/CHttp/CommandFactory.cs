using System.Buffers;
using System.CommandLine;
using System.Globalization;
using CHttp.Abstractions;
using CHttp.Binders;
using CHttp.Data;
using CHttp.Http;
using CHttp.Statitics;
using CHttp.Writers;

namespace CHttp;

internal static class CommandFactory
{
	public static Command CreateRootCommand(
		IWriter? writer = null,
		Abstractions.IConsole? console = null,
		IFileSystem? fileSystem = null)
	{
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
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
			description: """Headers Key-Value pairs separated by ':'. For example --header="key:myvalue" """);
		headerOptions.AddAlias("-h");
		headerOptions.IsRequired = false;
		headerOptions.Arity = ArgumentArity.ZeroOrMore;
		headerOptions.AllowMultipleArgumentsPerToken = true;

		var formsOptions = new Option<IEnumerable<string>>(
			name: "--form",
			getDefaultValue: Enumerable.Empty<string>,
			description: """Forms Key-Value pairs separated by ':' For example --form="name:myvalue" """);
		formsOptions.AddAlias("-f");
		formsOptions.IsRequired = false;
		formsOptions.Arity = ArgumentArity.ZeroOrMore;
		formsOptions.AllowMultipleArgumentsPerToken = true;

		var bodyOptions = new Option<string>(
			name: "--body",
			description: "Request body");
		bodyOptions.AddAlias("-b");
		bodyOptions.IsRequired = false;

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

		var cookieContainer = new Option<string>(
			name: "--cookie-container",
			getDefaultValue: () => string.Empty,
			description: "A file to share cookies among requests.");
		cookieContainer.IsRequired = false;

		var logOption = new Option<LogLevel>(
			name: "--log",
			getDefaultValue: () => LogLevel.Verbose,
			description: "Level of logging details.");
		logOption.AddAlias("-l");
		logOption.IsRequired = false;
		logOption.FromAmong(nameof(LogLevel.Quiet), nameof(LogLevel.Normal), nameof(LogLevel.Verbose));

		var outputFileOption = new Option<string>(
			name: "--output",
			getDefaultValue: () => string.Empty,
			description: "Output to file.");
		outputFileOption.AddAlias("-o");
		outputFileOption.IsRequired = false;

		var cOption = new Option<int>(
			name: "--clients",
			getDefaultValue: () => 20,
			description: "Number of parallel clients.");
		cOption.AddAlias("-c");
		cOption.IsRequired = false;

		var nOption = new Option<int>(
			name: "--requestCount",
			getDefaultValue: () => 100,
			description: "Number of total requests sent.");
		nOption.AddAlias("-n");
		nOption.IsRequired = false;

		var uploadThrottleOption = new Option<int?>(
			name: "--upload-throttle",
			getDefaultValue: () => null,
			description: "Specify HTTP level throttling in kbyte/sec when sending the request");
		nOption.IsRequired = false;

		var diffFileOption = new Option<IEnumerable<string>>(
			name: "--files",
			getDefaultValue: () => Enumerable.Empty<string>(),
			description: "List of 2 files to be compared.");
		outputFileOption.IsRequired = false;

		var metricsOption = new Option<string>(
			name: "--metrics",
			getDefaultValue: () => Environment.GetEnvironmentVariable("chttp_metrics", EnvironmentVariableTarget.Process) ?? string.Empty,
			description: "When Application Insights connection string is set, it pushes performance metrics data.");
		outputFileOption.IsRequired = false;

		var rootCommand = new RootCommand("Send HTTP request");
		rootCommand.AddGlobalOption(versionOptions);
		rootCommand.AddGlobalOption(methodOptions);
		rootCommand.AddGlobalOption(headerOptions);
		rootCommand.AddGlobalOption(timeoutOption);
		rootCommand.AddGlobalOption(redirectOption);
		rootCommand.AddGlobalOption(validateCertificateOption);
		rootCommand.AddGlobalOption(logOption);
		rootCommand.AddGlobalOption(outputFileOption);
		rootCommand.AddGlobalOption(cookieContainer);
		rootCommand.AddOption(bodyOptions);
		rootCommand.AddOption(uriOption);
		rootCommand.AddOption(uploadThrottleOption);

		CreateFormsCommand(writer, fileSystem, versionOptions, methodOptions, headerOptions, formsOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, outputFileOption, cookieContainer, rootCommand);

		CreateDefaultCommand(writer, fileSystem, versionOptions, methodOptions, headerOptions, bodyOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, logOption, outputFileOption, cookieContainer, uploadThrottleOption, rootCommand);

		CreateMeasureCommand(console, fileSystem, versionOptions, methodOptions, headerOptions, bodyOptions, timeoutOption, redirectOption, validateCertificateOption, uriOption, nOption, cOption, outputFileOption, metricsOption, cookieContainer, rootCommand);

		CreateDiffCommand(console, fileSystem, diffFileOption, rootCommand);

		return rootCommand;
	}

	private static void CreateFormsCommand(IWriter? writer,
		IFileSystem? fileSystem,
		Option<string> versionOptions,
		Option<string> methodOptions,
		Option<IEnumerable<string>> headerOptions,
		Option<IEnumerable<string>> formsOptions,
		Option<double> timeoutOption,
		Option<bool> redirectOption,
		Option<bool> validateCertificateOption,
		Option<string> uriOption,
		Option<LogLevel> logOption,
		Option<string> outputFileOption,
		Option<string> cookieContainerOption,
		RootCommand rootCommand)
	{
		var formsCommand = new Command("forms", "Forms request");
		formsCommand.AddOption(formsOptions);
		formsCommand.AddOption(uriOption);
		rootCommand.Add(formsCommand);
		formsCommand.SetHandler(async (requestDetails, httpBehavior, outputBehavior, forms) =>
		{
			writer ??= new WriterStrategy(outputBehavior);
			var cookieContainer = new PersistentCookieContainer(fileSystem ??= new FileSystem(), httpBehavior.CookieContainer);
			var client = new HttpMessageSender(writer, cookieContainer, httpBehavior);
			var formContent = new FormUrlEncodedContent(forms.Select(x => new KeyValuePair<string, string>(x.GetKey().ToString(), x.GetValue().ToString())));
			requestDetails = requestDetails with { Content = formContent };
			await client.SendRequestAsync(requestDetails);
			await writer.CompleteAsync(CancellationToken.None);
			await cookieContainer.SaveAsync();
		},
		new HttpRequestDetailsBinder(new HttpMethodBinder(methodOptions),
		  new UriBinder(uriOption),
		  new VersionBinder(versionOptions),
		  new KeyValueBinder(headerOptions)),
		new HttpBehaviorBinder(
		  new InvertBinder(redirectOption),
		  new InvertBinder(validateCertificateOption),
		  timeoutOption,
		  cookieContainerOption),
		new OutputBehaviorBinder(logOption, outputFileOption),
		new KeyValueBinder(formsOptions));
	}

	private static void CreateDefaultCommand(IWriter? writer,
		IFileSystem? fileSystem,
		Option<string> versionOptions,
		Option<string> methodOptions,
		Option<IEnumerable<string>> headerOptions,
		Option<string> bodyOptions,
		Option<double> timeoutOption,
		Option<bool> redirectOption,
		Option<bool> validateCertificateOption,
		Option<string> uriOption,
		Option<LogLevel> logOption,
		Option<string> outputFileOption,
		Option<string> cookieContainerOption,
		Option<int?> uploadThrottleOption,
		RootCommand rootCommand)
	{
		rootCommand.SetHandler(async (requestDetails, httpBehavior, outputBehavior, body, uploadThrottle) =>
		{
			fileSystem ??= new FileSystem();
			writer ??= new WriterStrategy(outputBehavior);
			var cookieContainer = new PersistentCookieContainer(fileSystem, httpBehavior.CookieContainer);
			var client = new HttpMessageSender(writer, cookieContainer, httpBehavior);
			if (uploadThrottle.HasValue && uploadThrottle.Value > 0)
				requestDetails = requestDetails with { Content = new UploadThrottledStringContent(body, uploadThrottle.Value, new Awaiter()) };
			else if (!string.IsNullOrEmpty(body))
				requestDetails = requestDetails with { Content = LoadBody(fileSystem, body) };
			await client.SendRequestAsync(requestDetails);
			await writer.CompleteAsync(CancellationToken.None);
			await cookieContainer.SaveAsync();
		},
		new HttpRequestDetailsBinder(new HttpMethodBinder(methodOptions),
		  new UriBinder(uriOption),
		  new VersionBinder(versionOptions),
		  new KeyValueBinder(headerOptions)),
		new HttpBehaviorBinder(
		  new InvertBinder(redirectOption),
		  new InvertBinder(validateCertificateOption),
		  timeoutOption,
		  cookieContainerOption),
		new OutputBehaviorBinder(logOption, outputFileOption),
		bodyOptions,
		uploadThrottleOption);
	}

	private static void CreateMeasureCommand(Abstractions.IConsole? console,
		IFileSystem? fileSystem,
		Option<string> versionOptions,
		Option<string> methodOptions,
		Option<IEnumerable<string>> headerOptions,
		Option<string> bodyOptions,
		Option<double> timeoutOption,
		Option<bool> redirectOption,
		Option<bool> validateCertificateOption,
		Option<string> uriOption,
		Option<int> nOption,
		Option<int> cOption,
		Option<string> outputFileOption,
		Option<string> metricsOption,
		Option<string> cookieContainerOption,
		RootCommand rootCommand)
	{
		var perfCommand = new Command("perf", "Performance Measure");
		rootCommand.Add(perfCommand);
		perfCommand.AddOption(nOption);
		perfCommand.AddOption(bodyOptions);
		perfCommand.AddOption(cOption);
		perfCommand.AddOption(uriOption);
		perfCommand.AddOption(metricsOption);
		perfCommand.SetHandler(async (requestDetails, httpBehavior, performanceBehavior, body, outputFile, metricsConnectionString) =>
		{
			fileSystem ??= new FileSystem();
			if (!string.IsNullOrWhiteSpace(body))
				requestDetails = requestDetails with { Content = LoadBody(fileSystem, body) };
			httpBehavior = httpBehavior with { ToUtf8 = false };
			console ??= new CHttpConsole();
			var cookieContainer = new PersistentCookieContainer(fileSystem, httpBehavior.CookieContainer);
			ISummaryPrinter printer = new StatisticsPrinter(console);
			if (!string.IsNullOrWhiteSpace(outputFile))
				printer = new CompositePrinter(printer, new FilePrinter(outputFile, fileSystem));
			if (!string.IsNullOrWhiteSpace(metricsConnectionString))
				printer = new CompositePrinter(printer, new AppInsightsPrinter(console, metricsConnectionString));
			var orchestrator = new PerformanceMeasureOrchestrator(printer, console, new Awaiter(), cookieContainer, performanceBehavior);
			await orchestrator.RunAsync(requestDetails, httpBehavior);
		},
		new HttpRequestDetailsBinder(new HttpMethodBinder(methodOptions),
		  new UriBinder(uriOption),
		  new VersionBinder(versionOptions),
		  new KeyValueBinder(headerOptions)),
		new HttpBehaviorBinder(
		  new InvertBinder(redirectOption),
		  new InvertBinder(validateCertificateOption),
		  timeoutOption,
		  cookieContainerOption),
		 new PerformanceBehaviorBinder(nOption, cOption),
		 bodyOptions,
		 outputFileOption,
		 metricsOption);
	}

	private static void CreateDiffCommand(Abstractions.IConsole? console, IFileSystem? fileSystem, Option<IEnumerable<string>> diffFileOption, RootCommand rootCommand)
	{
		var diffCommand = new Command("diff", "Compares to performance measurement files");
		diffCommand.AddOption(diffFileOption);
		rootCommand.Add(diffCommand);
		diffCommand.SetHandler(async (diffFiles) =>
		{
			console ??= new CHttpConsole();
			fileSystem ??= new FileSystem();
			var filesCount = diffFiles.Count();
			if (filesCount == 0)
				return;
			if (filesCount == 1)
			{
				var session = await PerformanceFileHandler.LoadAsync(fileSystem, diffFiles.First());
				await new StatisticsPrinter(console).SummarizeResultsAsync(session);
				return;
			}
			if (filesCount > 1)
			{
				var session0 = await PerformanceFileHandler.LoadAsync(fileSystem, diffFiles.First());
				var session1 = await PerformanceFileHandler.LoadAsync(fileSystem, diffFiles.Last());
				var comparer = new DiffPrinter(console);
				comparer.Compare(session0, session1);
			}
		},
		diffFileOption);
	}

	private static HttpContent LoadBody(IFileSystem fileSystem, string input)
	{
		if (!fileSystem.Exists(input))
			return new StringContent(input);
		var contentStream = fileSystem.Open(input, FileMode.Open, FileAccess.Read);
		var memoryStream = new MemoryStream();
		contentStream.CopyTo(memoryStream);
		memoryStream.Seek(0, SeekOrigin.Begin);
		return new ByteArrayContent(memoryStream.ToArray());
	}
}
