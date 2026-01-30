# CHttp Copilot Skill Guide

## Overview

This document describes how to use the CHttp Copilot skill for the CHttpTools project. The skill enables you to leverage GitHub Copilot for understanding and using the CHttp CLI tool - a powerful .NET-based HTTP request tool with performance measurement capabilities.

## Skill Description

The **CHttp Skill** provides comprehensive guidance on using the CHttp command-line tool for:
- Sending HTTP requests with support for HTTP/1.0, 1.1, 2, and 3
- Measuring HTTP performance and latency
- Load testing with configurable concurrency
- Comparing performance metrics
- Testing APIs with various headers and authentication methods
- Analyzing performance distributions

## What CHttp Does

CHttp is a .NET 10-based tool that allows you to:

1. **Send HTTP Requests**: Make HTTP/1.0, 1.1, 2, or 3 requests to any URL
2. **Performance Testing**: Measure response times and throughput with multiple concurrent clients
3. **Load Testing**: Stress test APIs with configurable parallelism
4. **Performance Comparison**: Compare two performance measurement sessions
5. **Batch Testing**: Use CHttpExec for CI/CD pipeline testing with assertions
6. **Cookie Management**: Persist and reuse cookies across requests
7. **Authentication**: Support for Kerberos authentication
8. **Throttling**: Limit upload speed for network simulation

## Installation

### Install as .NET Tool
```bash
dotnet tool install -g LaDeak.CHttp --prerelease
dotnet tool install -g LaDeak.CHttpExec --prerelease
```

### Install as Executable
Download the latest executable from the [Releases page](https://github.com/ladeak/Http3Tools/releases).

### Install VS Code Extension
Search for "CHttp" in the VS Code marketplace and install it.

## Core Commands

### 1. Basic HTTP Request
```bash
chttp --method GET --uri https://api.example.com/data
```

**Key Options:**
- `--method (-m)`: GET, POST, PUT, DELETE, HEAD, OPTIONS, CONNECT, TRACE
- `--uri (-u)`: Target URL (required)
- `--http-version (-v)`: 1.0, 1.1, 2, or 3 (default: 3)
- `--header (-h)`: Add headers (format: "Key:Value")
- `--body (-b)`: Request body or file path
- `--log (-l)`: Quiet, Normal, or Verbose (default: Verbose)
- `--no-cert-validation`: Skip certificate checks
- `--timeout (-t)`: Timeout in seconds (default: 30)

### 2. Performance Measurement
```bash
chttp perf --uri https://api.example.com --clients 10 --requestCount 1000 --output results.json
```

**Key Options:**
- `--clients (-c)`: Number of parallel clients (default: 20)
- `--requestCount (-n)`: Total requests to send (default: 100)
- `--output (-o)`: Save results to JSON file
- `--shared-sockethandler`: Use socket pooling

**Output Includes:**
- Mean/Median/Min/Max latencies
- Standard deviation
- 95th percentile
- Throughput (B/s)
- Requests per second
- Distribution histogram
- HTTP status code breakdown

### 3. Performance Comparison
```bash
chttp diff --files baseline.json --files current.json
```

Shows differences in performance metrics with visual indicators.

### 4. Batch Testing with CHttpExec
```bash
chttpexec tests/test.chttp
```

Use `.chttp` files with assertions for CI/CD integration:
```
# @name benchmark
# @clientsCount 10
# @requestCount 100
# @assert mean < 500ms stddev < 100ms successStatus == 100
GET https://api.example.com/data HTTP/2
```

## Common Use Cases

### Testing an API Endpoint
```bash
chttp --method GET --uri https://localhost:5001/api/users --log Quiet
```

### POST Request with JSON Body
```bash
# Save JSON to file first (due to CLI limitation)
chttp --method POST --uri https://api.example.com/users \
  --header="Content-Type:application/json" \
  --body payload.json
```

### Testing Different HTTP Versions
```bash
# Test with HTTP/3
chttp --http-version 3 --uri https://api.example.com

# Test with HTTP/2
chttp --http-version 2 --uri https://api.example.com

# Test with HTTP/1.1
chttp --http-version 1.1 --uri https://api.example.com
```

### Load Testing
```bash
chttp perf --uri https://api.example.com \
  --clients 20 \
  --requestCount 1000 \
  --output load-test-results.json
```

### Comparing Performance Changes
```bash
# Baseline from old code
chttp perf --uri https://api.example.com --clients 10 --requestCount 500 --output baseline.json

# Current implementation
chttp perf --uri https://api.example.com --clients 10 --requestCount 500 --output current.json

# Compare
chttp diff --files baseline.json --files current.json
```

### Testing with Custom Headers
```bash
chttp --method GET --uri https://api.example.com \
  --header="Authorization:Bearer token123" \
  --header="Custom-Header:value"
```

### Saving Responses to File
```bash
chttp --uri https://api.example.com/data --output response.json
```

### Using Kerberos Authentication
```bash
chttp --kerberos-auth --uri https://secure-api.example.com/data
```

### Simulating Network Throttling
```bash
chttp --uri https://api.example.com/large-file \
  --upload-throttle 512
```

## Assertion Parameters (for CHttpExec)

When using `.chttp` files with assertions:

- `mean`: Average response time
- `stddev`: Standard deviation of response times
- `error`: Error margin
- `median`: Median response time
- `min`: Minimum response time
- `max`: Maximum response time
- `throughput`: Data throughput
- `requestsec` (or `requestSec`): Requests per second
- `percentile95th`: 95th percentile response time
- `successStatus`: Count of successful HTTP responses

**Time Units:**
- `s` - Seconds
- `ms` - Milliseconds
- `us` - Microseconds
- `ns` - Nanoseconds

**Example:**
```
@assert mean < 1s stddev < 0.5s requestSec >= 1000 throughput > 1MB/s successStatus >= 95
```

## Performance Output Interpretation

```
RequestCount: 100, Clients: 10, Connections: 10
| Mean:          322,698 us   |
| StdDev:         80,236 us   |
| Median:        310,700 us   |
| Min:           198,700 us   |
| Max:           652,700 us   |
| 95th:          473,700 us   |
| Throughput:    114.016 KB/s |
| Req/Sec:      2,82E+04      |
```

- **Mean**: Average response time across all requests
- **StdDev**: Consistency of response times (lower is better)
- **Median**: Middle value of all response times
- **Min/Max**: Range of response times
- **95th**: Response time for 95% of requests (performance SLA metric)
- **Throughput**: Data transfer rate
- **Req/Sec**: Requests per second (throughput metric)

## Help & More Information

For complete command reference:
```bash
chttp --help
chttp perf --help
chttp diff --help
chttpexec --help
```

Visit the [GitHub repository](https://github.com/ladeak/Http3Tools) for issues, documentation, and releases.

## Integration Tips

1. **Use with Copilot CLI**: Ask Copilot to help you construct CHttp commands
2. **Automate with CHttpExec**: Create `.chttp` files for CI/CD pipelines
3. **Track Performance**: Save output files and use `diff` command to track improvements
4. **API Testing**: Use for quick validation of API endpoints during development
5. **Performance Debugging**: Compare performance across different scenarios or HTTP versions

## When to Ask Copilot About CHttp

You can ask Copilot to:
- Construct CHttp commands for specific scenarios
- Help interpret performance results
- Create `.chttp` test files with assertions
- Debug HTTP issues
- Compare performance metrics
- Optimize API endpoints based on CHttp measurements
- Troubleshoot connection issues
- Choose appropriate settings for load testing
