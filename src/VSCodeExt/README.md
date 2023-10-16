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

Send a simple HTTP/2 request GET request:

```http
GET https://{{baseUrl}} HTTP/2
```

Add headers and content:

```http
POST https://localhost:5001/jsonrequest HTTP/2
Content-Type:application/json

{
    "message": "hello world"
}
```

### Use variables

```
@path = delay
###
GET https://{{baseUrl}}/{{path}} HTTP/2
```

### Use named requests

```
###
# @name jsonSample
GET https://{{baseUrl}}/jsonresponse HTTP/2
```

### Parse and use response headers and json content

In the sample below the `message` json value is used from the response body of the *jsonSample* named request as the content of the *echo* request.

```
###
# @name jsonSample
GET https://{{baseUrl}}/jsonresponse HTTP/2

###
# echo
GET https://{{baseUrl}}/echo HTTP/2

{{jsonSample.response.body.message}}
```

## Performance Measurements

Set at least one of the following arguments for performance measurments:

```
# @clientsCount 10
# @requestCount 100
```

```
### Test
@path = /delay
@baseUrl = localhost:5001
@custom = comparison

###
# @name jsonSample
GET https://{{baseUrl}}/jsonresponse HTTP/2

###
# echo
GET https://{{baseUrl}}/echo HTTP/2

{{jsonSample.response.body.message}}

###
# @name {{custom}}
GET https://{{baseUrl}} HTTP/2

###
# @name {{custom}}
# @clientsCount 10
# @requestCount 100
GET https://{{baseUrl}} HTTP/2

###
# @name basePerformance
# @clientsCount 10
# @requestCount 100
POST https://{{baseUrl}}/post

{"data":"hello world"}
```

### Sample performance measurement Output:

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

## DIFF

Compare two *named* performance measurement results with the `DIFF` command:

```
###
# @name comparison
GET https://{{baseUrl}} HTTP/2

###
# @name basePerformance
# @clientsCount 10
# @requestCount 100
POST https://{{baseUrl}}/post

{"data":"hello world"}

###
DIFF basePerformance comparison
```