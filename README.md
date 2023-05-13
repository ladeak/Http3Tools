# Http3Tools

Tools to send HTTP requests. The tool is based on .NET7, for HTTP/3 uses msquic. Linux dotnet tool installations should get libmsquic package.

## Getting Started

### Install as a .NET Tool

To install as a dotnet tool:

```
dotnet tool install -g LaDeak.CHttp
```

### Install Executable

Download latest executables from this GitHub repository's Releases page.

### Run the Tool

Run command as:

```
chttp --method GET --uri https://localhost:5001
```

or 

```
chttp.exe --method GET --uri https://localhost:5001
```

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
  -h, --header <header>                              Headers Key-Value pairs separated by ':' []
  -u, --uri <uri> (REQUIRED)                         The URL of the resource
  -t, --timeout <timeout>                            Timeout in seconds. [default: 30]
  --no-redirects                                     Disables following redirects on requests [default: False]
  --no-cert-validation, --no-certificate-validation  Disables certificate validation [default: False]
  -l, --log <Normal|Quiet|Verbose>                   Level of logging details. [default: Verbose]
  --version                                          Show version information
  -?, -h, --help                                     Show help and usage information

Commands:
  forms  Forms request
  json   Json request
  perf   Performance Measure
```

### Performance Measurements

Set the number of *clients* used to send the number of *requestCount* requests. The tool executes the test and writes out basic statistical information about the collected data.

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
  -c, --clients <clients>                            Number of parallel clients. [default: 20]
  -v, --http-version <1.0|1.1|2|3>                   The version of the HTTP request: 1.0, 1.1, 2, 3 [default: 3]
  -m, --method                                       HTTP Method [default: GET]
  <CONNECT|DELETE|GET|HEAD|OPTIONS|POST|PUT|TRACE>
  -h, --header <header>                              Headers Key-Value pairs separated by ':' []
  -u, --uri <uri> (REQUIRED)                         The URL of the resource
  -t, --timeout <timeout>                            Timeout in seconds. [default: 30]
  --no-redirects                                     Disables following redirects on requests [default: False]
  --no-cert-validation, --no-certificate-validation  Disables certificate validation [default: False]
  -l, --log <Normal|Quiet|Verbose>                   Level of logging details. [default: Verbose]
  -?, -h, --help                                     Show help and usage information
```
