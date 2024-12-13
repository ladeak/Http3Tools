# CHttpTools

CHttp is a tool to send HTTP requests. The tool is based on .NET8 and .NET9, for HTTP/3 uses msquic. Linux dotnet tool installations should get libmsquic package.

## Getting Startedh

### Install as a .NET Tool

To install as a dotnet tool:

```
dotnet tool install -g LaDeak.CHttpExec --prerelease
```

### Install Executable

Download latest executables from this GitHub repository's [Releases page](https://github.com/ladeak/Http3Tools/releases).

### Install as Visual Studio Code Extension

Download from Visual Studio Code marketplace as [CHttp](https://marketplace.visualstudio.com/items?itemName=ladeak-net.ladeak-chttp).

### Run the Tool

Run command as:

```
chttp --method GET --uri https://localhost:5001
```

or 

```
chttp.exe --method GET --uri https://localhost:5001
```

When no HTTP version is specified explicitly HTTP/3 is used by default.

## Options

For available commands and options run the tool with the `--help` switch:

```
chttp --help
```

Currently supported commands and options:

```
Description:
  Send HTTP request

Usage:
  CHttp [command] [options]

Options:
  -v, --http-version <1.0|1.1|2|3>                   The version of the HTTP request: 1.0, 1.1, 2, 3 [default: 3]
  -m, --method                                       HTTP Method [default: GET]
  <CONNECT|DELETE|GET|HEAD|OPTIONS|POST|PUT|TRACE>
  -h, --header <header>                              Headers Key-Value pairs separated by ':'. For example
                                                     --header="key:myvalue"  []
  -t, --timeout <timeout>                            Timeout in seconds. [default: 30]
  --no-redirects                                     Disables following redirects on requests [default: False]
  --no-cert-validation, --no-certificate-validation  Disables certificate validation [default: False]
  -l, --log <Normal|Quiet|Verbose>                   Level of logging details. [default: Verbose]
  -o, --output <output>                              Output to file. []
  --cookie-container <cookie-container>              A file to share cookies among requests. []
  -b, --body <body>                                  Request body or a file path containing the request
  -u, --uri <uri> (REQUIRED)                         The URL of the resource
  --upload-throttle <upload-throttle>                Specify HTTP level throttling in kbyte/sec when sending the
                                                     request []
  -k, --kerberos-auth                                Use Kerberos Auth [default: False]
  --version                                          Show version information
  -?, -h, --help                                     Show help and usage information

Commands:
  forms  Forms request
  perf   Performance Measure
  diff   Compares to performance measurement files
```

### HTTP Version

The tool supports HTTP/1.0, HTTP/1.1, HTTP/2 and HTTP/3. Specify `1.0`, `1.1`, `2`, `3` values accordingly. For HTTP/3 requires support from the OS. On Windows this is available with recent Windows version and on Linux install the `libmsquic` package.

#### Log Levels

- Quiet: only a summary of the HTTP response is displayed.
- Normal: response headers and summary of the HTTP request is displayed.
- Verbose: response headers, content and summary of the HTTP request is displayed.

#### Cookie Containers

When multiple coherent requests would share a cookies, use a cookie containar. This cookie container will persist all cookies to a file, and updating this file after each request.

#### Upload Throttle

It is possible to throttle request content. Note, that this is only throttling HTTP level traffic. Specify the values in *kbyte/sec*.

#### Body

Use simple string bodies or a filepath as the input for the request.

> Note, that the current version of the tool does not allow sending JSON string in the body parameter. For more details see the linked [issue](https://github.com/dotnet/command-line-api/issues/1758). For sending JSON content, save the data to file and pass the filename as the argument of the body parameter.

### Performance Measurements

Set the number of *clients* (`-c`) used to send the number of *requestCount* (`-n`) requests. The tool executes the test and writes out basic statistical information about the collected data.

```
chttp perf --help
```

```
Description:
  Performance Measure

Usage:
  CHttp perf [options]

Options:
  -n, --requestCount <requestCount>                  Number of total requests sent. [default: 100]
  -b, --body <body>                                  Request body or a file path containing the request
  -c, --clients <clients>                            Number of parallel clients. [default: 20]
  -u, --uri <uri> (REQUIRED)                         The URL of the resource
  --metrics <metrics>                                When Application Insights connection string is set, it pushes
                                                     performance metrics data. []
  -v, --http-version <1.0|1.1|2|3>                   The version of the HTTP request: 1.0, 1.1, 2, 3 [default: 3]
  -m, --method                                       HTTP Method [default: GET]
  <CONNECT|DELETE|GET|HEAD|OPTIONS|POST|PUT|TRACE>
  -h, --header <header>                              Headers Key-Value pairs separated by ':'. For example
                                                     --header="key:myvalue"  []
  -t, --timeout <timeout>                            Timeout in seconds. [default: 30]
  --no-redirects                                     Disables following redirects on requests [default: False]
  --no-cert-validation, --no-certificate-validation  Disables certificate validation [default: False]
  -l, --log <Normal|Quiet|Verbose>                   Level of logging details. [default: Verbose]
  -o, --output <output>                              Output to file. []
  --cookie-container <cookie-container>              A file to share cookies among requests. []
  -?, -h, --help                                     Show help and usage information
```

Performance measurements yields results such as:

```
RequestCount: 100, Clients: 10, Connections: 10
| Mean:          322,698 us   |
| StdDev:         80,236 us   |
| Error:           8,024 us   |
| Median:        310,700 us   |
| Min:           198,700 us   |
| Max:           652,700 us   |
| 95th:          473,700 us   |
| Throughput:      0.000  B/s |
| Req/Sec:      2,82E+04      |
------------------------------------------------------------------------
   244,100 us #########
   289,500 us ###################
   334,900 us ###################
   380,300 us ##############
   425,700 us ####
   471,100 us ##
   516,500 us ##
   561,900 us #
   607,300 us 
   652,700 us #
------------------------------------------------------------------------
HTTP status codes:
1xx: 0, 2xx: 100, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
------------------------------------------------------------------------
```

The top section details standard statistical values. The middle section draws a distribution of the requests. The distribution is only rendered for performance measurements with at least 100 requests. The last section shows the response HTTP status codes of these results.

When results are persisted using the `-o` or `--output` parameters, the `diff` command can be used to compare results.

### Diff

```
Description:
  Compares to performance measurement files

Usage:
  CHttp diff [options]

Options:
 --files <files>                                    List of 2 files to be compared. []
```

Such as: 

```
diff --files session0.json --files session1.json
```

The command show the results of `session0.json` as the base and the results of `session1.json` as the difference to the base.

```
RequestCount: 100, Clients: 10
| Mean:          183.910 ms       -1.847 ms   |
| StdDev:        181.972 ms       +6.523 ms   |
| Error:          18.197 ms     +652.321 us   |
| Median:        114.454 ms       +1.057 ms   |
| Min:           102.744 ms     -575.000 us   |
| Max:           815.822 ms      +19.062 ms   |
| 95th:          735.611 ms       +7.440 ms   |
| Throughput:    114.016 KB/s     +3.903 KB/s |
| Req/Sec:          52.3          +0.826      |
------------------------------------------------------------------------------------------------------------------------
   175.441 ms ====================================================
   248.712 ms ==
   321.983 ms
   395.255 ms
   468.526 ms
   541.798 ms
   615.069 ms =+
   688.341 ms #
   761.612 ms =
   834.883 ms ===
------------------------------------------------------------------------------------------------------------------------
HTTP status codes:
1xx: 0 +0, 2xx: 100 +0, 3xx: 0 +0, 4xx: 0 +0, 5xx: 0 +0, Other: 0 +0
------------------------------------------------------------------------------------------------------------------------
```

The distribution section uses `=` sign to indicate that both sessions have results in a given bucket; `#` where the base session have more results and `+` where the comparison sesoion has more results in the bucket.

## CHttpExec

```
dotnet tool install -g LaDeak.CHttpExec --prerelease
```

CHttpExec is tool that execute HTTP queries and HTTP performance measurements from a `.chttp` file. This can be useful to execute it on CI server or on a test infrastructure. When using the tool make sure that both the test target and the test executor machines are consistent in the available resources term, and no other concurrent apps share these resources during the performance measuremnt. This includes the network between the target server and the test executor.

> Some automation infrastructure providers (such as GitHub Actions) might provide inconsistent amount of resources for different jobs.

Invoke the CLI as:

```$
chttpexec Http3Tools\tests\test.chttp
```

### Attributing the chttp file to assert performance requirements

A performance request can be attributed with `# @assert` expressions:

```
# @name validation
# @clientsCount 10
# @requestCount 100
# @assert mean < 1s stddev < 0.5s requestSec >= 0 throughput > 0 successStatus == 100
GET https://{{baseUrl}}/echo HTTP/2

{{nextRequest}}
```

Possible parameters to assert:

- mean
- stddev
- error
- median
- min
- max
- throughput
- requestsec
- percentile95th
- successStatus (the number of successful responses)

Values measured in time can be quantified by `s` `ms` `us` or `ns`. Values without a quantifier are processed as seconds.

Violations are reported as:

```$
ASSERTION VIOLATION
error: Mean is not < 1.000ns
error: StdDev is not < 0.001ns
```

## API usage

`MeasurementsSession` allows to apply the same logic used by the CHttp CLI, dotnet tool and VS Extension in C# projects.

```csharp
var session = new MeasurementsSession("https://localhost:5001");

for (int i = 0; i < 10; i++)
{
    session.StartMeasurement();
    // Execute measured code...
    session.EndMeasurement(HttpStatusCode.BadRequest);
}
await session.PrintStatsAsync();
```

- Use `DiffAsync` to create diffs from files - this allows to compare files programatically from sources of measurements.
- Use `SaveAsync` to save a measurement session into a file.
- Use `PrintStatsAsync` to print the results on the console.
- `GetSession` and `Diff` methods allow to compare measurement sessions in memory.

## Develop

Run the following commands to publish the native dependencis of the VS Code Extension and to copy them to the extension's dependencies:

```$
dotnet publish src/CHttpExtension -r win-x64
cp ./src/CHttpExtension/bin/Release/net9.0/win-x64/publish/* ./src/VSCodeExt/src/chttp-win-x64
```

### Cleanup NPM

- Clear npm cache `npm cache clean --force`
- Remove yeoman `npm install -g yo generator-code`
- Uninstall `npm install -g @vscode/vsce`
- Uninstall npm
