# Http3Repl

Tools to send HTTP requests. The tool is based on .NET7, for HTTP/3 uses msquic. Linux dotnet tool installations should get libmsquic package.

## Getting Started

To install as a dotnet tool:

```
dotnet tool install -g LaDeak.CHttp --prerelease
```

Run command as:

```
chttp --method GET --uri https://localhost:5001
```

## Options

Description:
  Send HTTP request

Usage:
  chttp [command] [options]

Options:
  -v, --http-version <1.0|1.1|2|3>                               The version of the HTTP request: 1.0, 1.1, 2, 3 [default: 3]
  -m, --method <CONNECT|DELETE|GET|HEAD|OPTIONS|POST|PUT|TRACE>  HTTP Method [default: GET]
  -h, --header <header>                                          Headers Key-Value pairs separated by ':' []
  -u, --uri <uri> (REQUIRED)                                     The URL of the resource
  -t, --timeout <timeout>                                        Timeout in seconds. [default: 30]
  --no-redirects                                                 Disables following redirects on requests [default: False]
  --no-cert-validation, --no-certificate-validation              Disables certificate validation [default: False]
  --version                                                      Show version information
  -?, -h, --help                                                 Show help and usage information

Commands:
  forms  Forms request
  json   Json request