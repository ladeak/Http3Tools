using CHttp.Data;

namespace CHttp.Tests.Data;

public class KeyValueDescriptorTests
{
    [Theory]
    [InlineData("key", "value")]
    [InlineData("api", ".efghefs==")]
    public void GivenHeader_CutsHeaderKeyAndValue(string key, string value)
    {
        var sut = new KeyValueDescriptor($"{key}:{value}");
        Assert.True(sut.GetKey().SequenceEqual(key));
        Assert.True(sut.GetValue().SequenceEqual(value));
    }
}
