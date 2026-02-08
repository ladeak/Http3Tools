namespace CHttpServer.Tests;

public class FeatureCollectionTests
{
    [Fact]
    public void AddAndGetFeature()
    {
        var features = new FeatureCollection();
        features.Add("test");
        Assert.Equal(1, features.Revision);
        var feature = features.Get<string>();
        Assert.Equal(1, features.Revision);
        Assert.Equal("test", feature);
        Assert.Equal(1, features.Revision);
    }

    [Fact]
    public void AddAddAndGetFeature()
    {
        var features = new FeatureCollection();
        features.Add("test");
        features.Add("test2");
        var feature = features.Get<string>();
        Assert.Equal("test", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void SetSetAndGetFeature()
    {
        var features = new FeatureCollection();
        features.Set("test");
        features.Set("test2");
        var feature = features.Get<string>();
        Assert.Equal("test2", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void SetAndAddGetFeature()
    {
        var features = new FeatureCollection();
        features.Set("test");
        features.Add("test2");
        var feature = features.Get<string>();
        Assert.Equal("test", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void AddAndSetGetFeature()
    {
        var features = new FeatureCollection();
        features.Add("test");
        features.Set("test2");
        var feature = features.Get<string>();
        Assert.Equal("test2", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void IsReadOnly()
    {
        var features = new FeatureCollection();
        Assert.False(features.IsReadOnly);
        features.Checkpoint();
        Assert.False(features.IsReadOnly);
        features.Add("test");
        Assert.False(features.IsReadOnly);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void MultiFeatureGetFeature()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollection();
        features.Set("test");
        features.Add(testDate);
        var feature = features.Get<string>();
        Assert.Equal("test", feature);
        var dateFeature = features.Get<DateTime>();
        Assert.Equal(testDate, dateFeature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void CheckpointClassResetFeature()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollection();
        features.Set("test");
        features.Checkpoint();
        features.Set(testDate);
        Assert.Equal(3, features.Revision);
        features.ResetCheckpoint();
        var feature = features.Get<string>();
        Assert.Equal("test", feature);
        Assert.Equal(default, features.Get<DateTime>());
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void CheckpointStructResetFeature()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollection();
        features.Set(testDate);
        features.Checkpoint();
        features.Set("test");
        Assert.Equal(3, features.Revision);
        features.ResetCheckpoint();
        Assert.Null(features.Get<string>());
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void RemoveString()
    {
        var features = new FeatureCollection();
        features.Set("test");
        features.Checkpoint();
        features.Set("test2");
        features.ResetCheckpoint();
        Assert.Equal("test", features.Get<string>());
    }

    [Fact]
    public void EmptyCheckpointString()
    {
        var features = new FeatureCollection();
        features.Checkpoint();
        features.Set("test");
        features.ResetCheckpoint();
        Assert.Null(features.Get<string>());
    }

    [Fact]
    public void NullableValueType()
    {
        DateTime? testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollection();
        features.Set(testDate);
        features.Checkpoint();
        Assert.Equal(testDate, features.Get<DateTime?>());
        DateTime? testDate2 = new DateTime(2025, 06, 25);
        features.Set(testDate2);
        features.ResetCheckpoint();
        Assert.Equal(testDate, features.Get<DateTime?>());
    }

    [Fact]
    public void EnumerateFeatures()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollection();
        Assert.True(features.SequenceEqual([]));
        features.Set(testDate);
        features.Checkpoint();
        features.Set("test");
        Assert.True(features.SequenceEqual([new(typeof(string), "test"), new(typeof(DateTime), testDate)]));
        features.ResetCheckpoint();
        Assert.True(features.SequenceEqual([new(typeof(DateTime), testDate)]));

        features.Set("test");
        Assert.True(features.SequenceEqual([new(typeof(string), "test"), new(typeof(DateTime), testDate)]));
        features.ResetCheckpoint();
        Assert.True(features.SequenceEqual([new(typeof(DateTime), testDate)]));
    }

    [Fact]
    public void Copy()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollection();
        features.Set(testDate);
        features.Set("test");
        var copy = features.Copy();
        features.Set<string>("test2");
        features.Set<DateTime>(default);

        Assert.True(copy.SequenceEqual([new(typeof(DateTime), testDate), new(typeof(string), "test")]));
        Assert.True(features.SequenceEqual([new(typeof(DateTime), default(DateTime)), new(typeof(string), "test2")]));
        copy.ResetCheckpoint();
        Assert.True(copy.SequenceEqual([]));
    }

    [Fact]
    public void CopyAfterToContextAwareShouldNotLooseType()
    {
        var features = new FeatureCollection();
        var contextAware = features.ToContextAware<object>();
        var copy = contextAware.Copy();
        Assert.IsType<FeatureCollection<object>>(copy);
    }

    [Fact]
    public void ImmutableFeatureCollection()
    {
        // Server
        var featureCollection = new FeatureCollection();
        featureCollection.AddRange(
            (typeof(F0), Feature),
            (typeof(F1), Feature));

        // ToContextAware
        var contextFeatures = featureCollection.ToContextAware<object>();

        // Connection
        contextFeatures.Add<F3>(Feature);
        contextFeatures.Add<F4>(Feature);

        // Copy for stream
        var streamFeatures = contextFeatures.Copy<FeatureCollection<object>>();

        // Stream Add
        streamFeatures.AddRange(
            (typeof(F5), Feature),
            (typeof(F6), Feature),
            (typeof(F7), Feature),
            (typeof(F8), Feature),
            (typeof(F9), Feature),
            (typeof(F10), Feature),
            (typeof(F11), Feature));

        // Stream Checkpoint
        streamFeatures.Checkpoint();

        for (int i = 0; i < 2; i++)
        {
            // Add more
            streamFeatures.Add<F12>(Feature);
            streamFeatures.Add<F13>(Feature);
            streamFeatures.Add<F14>(Feature);
            streamFeatures.Add<F15>(Feature);

            // Then retrieve
            Assert.NotNull(streamFeatures.Get<F0>());
            Assert.NotNull(streamFeatures.Get<F1>());
            Assert.NotNull(streamFeatures.Get<F3>());
            Assert.NotNull(streamFeatures.Get<F4>());
            Assert.NotNull(streamFeatures.Get<F5>());
            Assert.NotNull(streamFeatures.Get<F6>());
            Assert.NotNull(streamFeatures.Get<F7>());
            Assert.NotNull(streamFeatures.Get<F8>());
            Assert.NotNull(streamFeatures.Get<F9>());
            Assert.NotNull(streamFeatures.Get<F10>());
            Assert.NotNull(streamFeatures.Get<F11>());
            Assert.NotNull(streamFeatures.Get<F12>());
            Assert.NotNull(streamFeatures.Get<F13>());
            Assert.NotNull(streamFeatures.Get<F14>());
            Assert.NotNull(streamFeatures.Get<F14>());
            Assert.NotNull(streamFeatures.Get<F15>());
            Assert.NotNull(streamFeatures.Get<F15>());
            Assert.NotNull(streamFeatures.Get<F15>());
            Assert.NotNull(streamFeatures.Get<F15>());

            // Reset to checkpoint
            streamFeatures.ResetCheckpoint();
        }
    }

    private interface F0 { }
    private interface F1 { }
    private interface F2 { }
    private interface F3 { }
    private interface F4 { }
    private interface F5 { }
    private interface F6 { }
    private interface F7 { }
    private interface F8 { }
    private interface F9 { }
    private interface F10 { }
    private interface F11 { }
    private interface F12 { }
    private interface F13 { }
    private interface F14 { }
    private interface F15 { }
    private interface F16 { }
    private interface F17 { }
    private interface F18 { }
    private interface F19 { }
    private class SampleFeature : F0, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19 { }

    private static SampleFeature Feature { get; } = new SampleFeature();
}
