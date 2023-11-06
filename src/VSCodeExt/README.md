# CHttp Extension

VSCode Extension for CHttp command line tool. It allows to send and performance measure HTTP requests in Visual Studio Code. 

## Features

- Send/Cancel HTTP requests and view responses
- Compose requests using the chttp file and HTTP language
- Preview response with headers, body, trailers and a summary line
- Performance Measurement requests
- DIFF performance measurement results
- Use variables in request
- Define file scoped variables
- Use overarching cookie container
- Comments (line starts with # or //) support
- CodeLens for sending requests
- HTTP versions of 1.0 1.1 and 2 are supported

## Getting Started

Send a simple HTTP/2 and HTTP/3 request requests. When no HTTP version is specified HTTP/2 is used by default.

```http
GET https://localhost:5001
```

One can define headers and content. Separate headers and content with an empty line.

```http
POST https://localhost:5001/jsonrequest HTTP/2
Content-Type:application/json

{
    "message": "hello world"
}
```

To add comments use `#`or `//` characters. To separate queries, use the `###` character combination.

## Variables

It is possible to define and re-use file level variables using the `@variable` syntax. Then a variable can be referenced with the `{{variable}}` syntax.

```http
@baseUrl = localhost:5001
@path = delay
###
GET https://{{baseUrl}}/{{path}} HTTP/2
```

Variables may also reference another request's response content or headers. To achieve this, create a *named* request:

```http
###
# @name jsonSample
GET https://localhost:5001/jsonresponse
###
@nextRequestContent = {{jsonSample.response.body.message}}
###
# echo
GET https://localhost:5001/echo

{{nextRequestContent}}
```

In the above case the top request is named by the `# @name jsonSample` attribute. A variable `@nextRequestContent` references the response body's message JSON field. Use json-path to refer to a custom element of the response. Use the `[requestname].response.header.content-type` to refer to response headers.

Then use this variable in another request with `{{nextRequestContent}}` reference.

## Attributes

Use attributes to change the HTTP request behavior. At the time of writing the following attributes are supported:

- clientscount
- requestcount
- timeout (in seconds)
- no-certificate-validation
- no-redirect
- name
- kerberos-auth

Using named requests:

```
@baseUrl = localhost:5001
###
# @name jsonSample
GET https://{{baseUrl}}/jsonresponse HTTP/2
```

Or ignore certificate validation errors, by using the `@no-certificate-validation` attribute with value `true`.

```http
# @no-certificate-validation true
GET https://localhost:5001/endpoint HTTP/1.1
```

## Performance Measurements

Run performance measurements by defining either `@clientscount` or `@requestcount` attributes. Their default values are 10 and 100 respectively. 

```
# @clientsCount 10
# @requestCount 100
```

Samples:

```http
@baseUrl = localhost:5001

###
# @clientsCount 10
# @requestCount 100
GET https://{{baseUrl}}

###
# @clientsCount 10
# @requestCount 100
POST https://{{baseUrl}}/post

{"data":"hello world"}
```


While it is possible to set a request content, for performance measurements it is not suggested as larger requests can impact the performance measurement on client side.

### Sample performance measurement Output

The output has three sections. The top section displays statistical results. The middle section draws a distribution of requests. The last section displays an aggregate view of the response codes.

```
RequestCount: 100, Clients: 10
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

I this case the mean execution time is 322.698 us. The distribution has a single mode. All requests 100 returned status codes from the 200-299 range.

## Diff Performance Measurements

Compare two *named* performance measurement results with the `DIFF` command. Use the `@name [value]` attribute to give a referrable name to a query and refer these names with the `DIFF` command.

```
###
# @name comparison
# @clientsCount 10
# @requestCount 100
GET https://{{baseUrl}}/post HTTP/2

###
# @name basePerformance
# @clientsCount 10
# @requestCount 100
POST https://{{baseUrl}}/post

{"data":"hello world"}

###
DIFF basePerformance comparison
```

Diff results an aggregate view of the differences between the two requests.

```
RequestCount: 100, Clients: 10
| Mean:          448,127 us      -55,265 us   |
| StdDev:        151,588 us     +120,312 us   |
| Error:          15,159 us      +12,031 us   |
| Median:        413,600 us     -115,100 us   |
| Min:           265,300 us     -137,600 us   |
| Max:             1,285 ms     +225,800 us   |
| 95th:          763,700 us     +220,100 us   |
| Throughput:      0.000  B/s          0  B/s |
| Req/Sec:      1,16E+04       +9244,993      |
------------------------------------------------------------------------
   266,040 us +++++++++++++
   404,380 us =============###
   542,720 us ====############
   681,060 us =++
   819,400 us ##
   957,740 us =
     1,096 ms =+
     1,234 ms 
     1,373 ms 
     1,511 ms +
------------------------------------------------------------------------
HTTP status codes:
1xx: 0 +0, 2xx: 100 +0, 3xx: 0 +0, 4xx: 0 +0, 5xx: 0 +0, Other: 0 +0
------------------------------------------------------------------------
*Warning: session files contain different urls: https://localhost:5001/,https://localhost:5001/post
------------------------------------------------------------------------
```