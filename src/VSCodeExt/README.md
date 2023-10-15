# LaDeakVSExt

VSCode Extension for CHttp command line tool


```
### Test
@path = /delay
@baseUrl = localhost:5001
@custom = hello

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
# @name basePerformance.json
# @clientsCount 10
# @requestCount 100
POST https://{{baseUrl}}/post

{"data":"hello world"}

###
DIFF hello basePerformance.json
```

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