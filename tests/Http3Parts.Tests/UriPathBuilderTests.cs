//using Http3Parts.UriBuilders;

//namespace Http3Parts.Tests;

//public class UriPathBuilderTests
//{
//    private const string BaseUrl = "https://localhost:5000";
//    private const string BaseUrlSlash = $"{BaseUrl}/";

//    [Fact]
//    public void String()
//    {
//        string segment = "1";
//        Assert.Equal($"{BaseUrl}/a/{segment}/a", UriPathBuilder.Create($"{BaseUrl}/a/{segment}/a"));
//        Assert.Equal($"/a/{segment}/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/{segment}", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/a/{segment}", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/{segment}", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"{segment}", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void SlashString()
//    {
//        string segment = "/1";
//        Assert.Equal($"{BaseUrl}/a/1/a", UriPathBuilder.Create($"{BaseUrl}/a/{segment}/a"));
//        Assert.Equal($"/1/a", UriPathBuilder.Create($"{segment}/a"));
//        Assert.Equal($"/1", UriPathBuilder.Create($"{segment}"));
//        Assert.Equal($"/1/", UriPathBuilder.Create($"{segment}/"));
//        Assert.Equal($"/1", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"/1", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void StringSlash()
//    {
//        string segment = "1/";
//        Assert.Equal($"/a/1/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/1/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/1/", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/a//1/", UriPathBuilder.Create($"/a//{segment}"));
//        Assert.Equal($"/1/", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"1/", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void Int()
//    {
//        int segment = 1;
//        Assert.Equal($"/a/1/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/1/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/1", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/a/1", UriPathBuilder.Create($"/a{segment}"));
//        Assert.Equal($"/1", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"1", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void Double()
//    {
//        double segment = 1;
//        Assert.Equal($"/a/1/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/1/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/1", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/a/1", UriPathBuilder.Create($"/a{segment}"));
//        Assert.Equal($"/1", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"1", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void DoubleFormat()
//    {
//        double segment = 1;
//        Assert.Equal($"{BaseUrl}/a/{segment:0.00}/a", UriPathBuilder.Create($"{BaseUrl}/a/{segment:0.00}/a"));
//    }

//    [Fact]
//    public void DateTime()
//    {
//        DateTime segment = new DateTime(2024, 07, 21);
//        Assert.Equal($"{BaseUrl}/a/{segment}/a", UriPathBuilder.Create($"{BaseUrl}/a/{segment}/a"));
//    }

//    [Fact]
//    public void DateTimeFormatO()
//    {
//        DateTime segment = new DateTime(2024, 07, 21);
//        Assert.Equal($"{BaseUrl}/a/{segment:O}/a", UriPathBuilder.Create($"{BaseUrl}/a/{segment:O}/a"));
//    }

//    [Fact]
//    public void MyFormattableTest()
//    {
//        MyFormattable segment = new(false, false);
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}a"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/segment", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/segment", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"segment", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void MySpanFormattableTest()
//    {
//        MySpanFormattable segment = new(false, false);
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}a"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/segment", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/segment", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"segment", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void MyFormattableSlashEndTest()
//    {
//        MyFormattable segment = new(false, true);
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}a"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/segment/", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"segment/", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void MySpanFormattableSlashEndTest()
//    {
//        MySpanFormattable segment = new(false, true);
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}a"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/segment/", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"segment/", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void MyFormattableSlashBeginTest()
//    {
//        MyFormattable segment = new(true, false);
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}a"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/segment", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/segment", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"/segment", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void MySpanFormattableSlashBeginTest()
//    {
//        MySpanFormattable segment = new(true, false);
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}/a"));
//        Assert.Equal($"/a/segment/a", UriPathBuilder.Create($"/a/{segment}a"));
//        Assert.Equal($"/a/segment/", UriPathBuilder.Create($"/a/{segment}/"));
//        Assert.Equal($"/a/segment", UriPathBuilder.Create($"/a/{segment}"));
//        Assert.Equal($"/segment", UriPathBuilder.Create($"/{segment}"));
//        Assert.Equal($"/segment", UriPathBuilder.Create($"{segment}"));
//    }

//    [Fact]
//    public void NoSlash()
//    {
//        int segment = 1;
//        Assert.Equal($"{BaseUrl}/a/{segment}/a", UriPathBuilder.Create($"{BaseUrl}a{segment}a"));
//    }

//    [Fact]
//    public void AllDoubleSlashInt()
//    {
//        int segment = 1;
//        string baseUrl = BaseUrl + '/';
//        Assert.Equal($"{BaseUrl}/a/{segment}/a", UriPathBuilder.Create($"{baseUrl}/a/{segment}/a"));
//    }

//    [Fact]
//    public void AllDoubleSlashMySpanFormattable()
//    {
//        MySpanFormattable segment = new(true, true);
//        string baseUrl = BaseUrl + '/';
//        Assert.Equal($"{BaseUrl}/a/segment/a", UriPathBuilder.Create($"{baseUrl}/a/{segment}/a"));
//    }

//    [Fact]
//    public void AllDoubleSlashMyFormattable()
//    {
//        MyFormattable segment = new(true, true);
//        string baseUrl = BaseUrl + '/';
//        Assert.Equal($"{BaseUrl}/a/segment/a", UriPathBuilder.Create($"{baseUrl}/a/{segment}/a"));
//    }

//    [Fact]
//    public void QueryString()
//    {
//        string segment = "/a";
//        Assert.Equal($"{BaseUrl}/a?a=//a", UriPathBuilder.Create($"{BaseUrl}/a?a=/{segment:ordinal}"));
//        Assert.Equal($"{BaseUrl}/a?a=/a", UriPathBuilder.Create($"{BaseUrl}/a?a={segment}"));
//    }

//    [Fact]
//    public void CanonicalString()
//    {
//        string segment = "/1";
//        string query = "?a=/a/b/..&=013";
//        Assert.Equal($"{BaseUrl}/b/c/1/a", UriPathBuilder.CreateCanonical($"{BaseUrl}/a/../b/./c/{segment}/a"));
//        Assert.Equal($"{BaseUrl}/b/c/1/a?a=/../", UriPathBuilder.CreateCanonical($"{BaseUrl}/a/../b/./c/{segment}/a?a=/../"));
//        Assert.Equal($"/a", UriPathBuilder.CreateCanonical($"{segment}../a"));
//        Assert.Equal($"/1/", UriPathBuilder.CreateCanonical($"{segment}./"));
//        Assert.Equal($"/1/", UriPathBuilder.CreateCanonical($"{segment}/./"));
//        Assert.Equal($"/", UriPathBuilder.CreateCanonical($"{segment}/./.."));
//        Assert.Equal($"/1", UriPathBuilder.CreateCanonical($"/.././{segment}"));
//        Assert.Equal($"/1", UriPathBuilder.CreateCanonical($"/./../{segment}"));
//        Assert.Equal($"/", UriPathBuilder.CreateCanonical($"/./{segment}/./../"));
//        Assert.Equal($"{BaseUrl}/a/b/1/", UriPathBuilder.CreateCanonical($"{BaseUrlSlash}/a/b/{segment}/a/../"));
//        Assert.Equal($"{BaseUrl}/a/b/1/", UriPathBuilder.CreateCanonical($"{BaseUrl}a/b/{segment}/a/.."));
//        Assert.Equal($"{BaseUrl}/a/b/1/{query}", UriPathBuilder.CreateCanonical($"{BaseUrlSlash}/a/b/{segment}/a/../{query}"));
//        Assert.Equal($"{BaseUrl}/a/b/1/{query}", UriPathBuilder.CreateCanonical($"{BaseUrl}a/b/{segment}/a/..{query}"));
//        Assert.Equal($"{BaseUrl}/a/b/1/a/{query}", UriPathBuilder.CreateCanonical($"{BaseUrl}a/b/{segment}/a/./{query}"));
//        Assert.Equal($"{BaseUrl}/a/b/1/a/{query}", UriPathBuilder.CreateCanonical($"{BaseUrl}a/b/{segment}/a/.{query}"));
//    }

//    [Fact]
//    public void Test()
//    {
//        string segment = "/someId";
//        Assert.Equal("https://localhost:5000/path/entity/someId", UriPathBuilder.Create($"https://localhost:5000/path/entity/{segment}"));
//        Assert.Equal("https://localhost:5000/path/entity/someId", UriPathBuilder.Create($"https://localhost:5000/path/entity/{segment}"));
//    }

//    private struct MyFormattable : IFormattable
//    {
//        private readonly bool _beginSlash = false;
//        private readonly bool _endSlash = false;

//        public MyFormattable(bool beginSlash, bool endSlash)
//        {
//            _beginSlash = beginSlash;
//            _endSlash = endSlash;
//        }

//        public string ToString(string? format, IFormatProvider? formatProvider)
//        {
//            var begin = _beginSlash ? "/" : null;
//            var end = _endSlash ? "/" : null;
//            return $"{begin}segment{end}";
//        }
//    }

//    private struct MySpanFormattable : ISpanFormattable
//    {
//        private readonly bool _beginSlash;
//        private readonly bool _endSlash;

//        public MySpanFormattable()
//        {
//        }

//        public MySpanFormattable(bool beginSlash, bool endSlash)
//        {
//            _beginSlash = beginSlash;
//            _endSlash = endSlash;
//        }

//        public string ToString(string? format, IFormatProvider? formatProvider)
//        {
//            var begin = _beginSlash ? "/" : null;
//            var end = _endSlash ? "/" : null;
//            return $"{begin}segment{end}";
//        }

//        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
//        {
//            var begin = _beginSlash ? "/" : null;
//            var end = _endSlash ? "/" : null;
//            var text = $"{begin}segment{end}";
//            charsWritten = text.Length;
//            return text.TryCopyTo(destination);
//        }
//    }
//}