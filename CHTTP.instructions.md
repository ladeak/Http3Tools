# CHttp CLI Tool Skill

You have access to the CHttp tool, a powerful command-line utility for sending HTTP requests and measuring performance. This skill provides guidance on how to effectively use CHttp for various HTTP operations.

## What is CHttp?

CHttp is a .NET-based tool for sending HTTP requests with support for HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3. It can perform:
- Single HTTP requests
- Performance measurements and load testing
- Performance data comparison
- Cookie persistence across requests
- Kerberos authentication
- Request throttling

## Basic Command Syntax

```bash
chttp [command] [options]
```

## Common HTTP Request Options

### Required Options
- `--uri <URL>` (or `-u`): The target URL (REQUIRED)

### HTTP Configuration
- `--http-version <1.0|1.1|2|3>` (or `-v`): HTTP version to use [default: 3]
- `--method <GET|POST|PUT|DELETE|HEAD|OPTIONS|CONNECT|TRACE>` (or `-m`): HTTP method [default: GET]
- `--header` (or `-h`): Add headers as key:value pairs (can be used multiple times)
  - Example: `--header="Content-Type:application/json"`

### Request Body & Content
- `--body` (or `-b`): Request body or path to file containing the request body
  - Note: JSON strings must be saved to a file, not passed directly
- `--upload-throttle`: Throttle upload speed in KB/s

### Response & Behavior
- `--timeout` (or `-t`): Timeout in seconds [default: 30]
- `--no-redirects`: Disable automatic redirect following
- `--no-cert-validation` or `--no-certificate-validation`: Skip certificate validation (useful for testing)
- `--log <Normal|Quiet|Silent|Verbose>` (or `-l`): Logging level [default: Verbose]
  - Quiet: Only summary
  - Normal: Headers and summary
  - Verbose: Headers, content, and summary
- `--output` (or `-o`): Save response to file

### Advanced Options
- `--cookie-container`: Path to file for sharing cookies across requests
- `--kerberos-auth` (or `-k`): Enable Kerberos authentication

## Examples

### Basic GET Request
```bash
chttp --http-version 2 --method GET --uri https://api.example.com/data
```

### POST Request with Headers and Body
```bash
chttp --http-version 2 --method POST --uri https://api.example.com/data --header="Content-Type:application/json" --body request.json
```

### Using HTTP/2 (Default for this Skill)
```bash
chttp --http-version 2 --method GET --uri https://api.example.com
```

### Quiet Output
```bash
chttp --http-version 2 --log Quiet --uri https://api.example.com
```

## Performance Measurement Command

Measure performance by sending multiple requests with multiple concurrent clients.

```bash
chttp perf [options]
```

### Performance-Specific Options
- `--clients` (or `-c`): Number of parallel clients [default: 20]
- `--requestCount` (or `-n`): Total number of requests [default: 100]
- `--output` (or `-o`): Save results to JSON file
- `--shared-sockethandler`: Use socket pooling for multiple connections
- `--metrics`: Publish metrics to gRPC OpenTelemetry endpoint

### Performance Example
```bash
chttp perf --http-version 2 --uri https://api.example.com --clients 10 --requestCount 1000 --output results.json
```

### Performance Output Includes
- Mean, Standard Deviation, Median, Min, Max latencies
- 95th percentile latency
- Throughput (B/s)
- Requests per second
- Distribution histogram
- HTTP status code breakdown

## Performance Comparison Command

Compare two performance measurement files.

```bash
chttp diff --files session1.json --files session2.json
```

This shows session1 as baseline and differences in session2, using:
- `=` for similar results in both sessions
- `#` for more results in baseline
- `+` for more results in comparison session

## CHttpExec - Batch Testing

For CI/CD pipelines, use CHttpExec with `.chttp` files:

```bash
chttpexec tests/test.chttp
```

### .chttp File Format with Assertions

```
# @name validation
# @clientsCount 10
# @requestCount 100
# @assert mean < 1s stddev < 0.5s requestSec >= 0 throughput > 0 successStatus == 100
GET https://{{baseUrl}}/echo HTTP/2

{{nextRequest}}
```

### Assertion Parameters
- `mean`: Average latency
- `stddev`: Standard deviation
- `error`: Error margin
- `median`: Median latency
- `min`/`max`: Min/max latencies
- `throughput`: Data throughput
- `requestsec`: Requests per second
- `percentile95th`: 95th percentile latency
- `successStatus`: Count of successful responses

### Time Units
- `s`: Seconds
- `ms`: Milliseconds
- `us`: Microseconds
- `ns`: Nanoseconds

## When to Use CHttp

Use this skill when the user wants to:
- Send HTTP requests from the command line
- Test API endpoints
- Measure HTTP performance and latency
- Compare performance across different scenarios
- Load test a server with configurable concurrency
- Debug HTTP interactions with custom headers and timeouts
- Validate HTTP status codes and responses
- Test different HTTP versions (1.0, 1.1, 2, 3)
- Authenticate using Kerberos
- Manage cookies across multiple requests
- Analyze performance distributions and throughput

## Integration with Development

CHttp can be installed as:
1. A .NET global tool: `dotnet tool install -g LaDeak.CHttp --prerelease` and `dotnet tool install -g LaDeak.CHttpExec --prerelease`
2. A standalone executable (from releases)
3. A Visual Studio Code extension

For C# projects, use the `MeasurementsSession` API directly to embed performance measurement logic.

## Common Use Cases

1. **Quick API Testing**: Verify endpoint responses with different methods and headers
2. **Performance Baseline**: Establish baseline performance metrics for an API
3. **Regression Testing**: Compare current performance against baseline measurements
4. **Load Testing**: Stress test endpoints with configurable concurrency
5. **HTTP Version Testing**: Compare performance across different HTTP versions
6. **CI/CD Integration**: Use CHttpExec for automated performance validation
