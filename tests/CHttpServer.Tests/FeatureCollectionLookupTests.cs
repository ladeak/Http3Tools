namespace CHttpServer.Tests;

public class FeatureCollectionLookupTests
{
    [Fact]
    public void AddAndGetFeature()
    {
        var features = new FeatureCollectionLookup();
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
        var features = new FeatureCollectionLookup();
        features.Add("test");
        features.Add("test2");
        var feature = features.Get<string>();
        Assert.Equal("test2", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void SetSetAndGetFeature()
    {
        var features = new FeatureCollectionLookup();
        features.Set("test");
        features.Set("test2");
        var feature = features.Get<string>();
        Assert.Equal("test2", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void SetAndAddGetFeature()
    {
        var features = new FeatureCollectionLookup();
        features.Set("test");
        features.Add("test2");
        var feature = features.Get<string>();
        Assert.Equal("test2", feature);
        Assert.Equal(2, features.Revision);
    }

    [Fact]
    public void IsReadOnly()
    {
        var features = new FeatureCollectionLookup();
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
        var features = new FeatureCollectionLookup();
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
        var features = new FeatureCollectionLookup();
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
        var features = new FeatureCollectionLookup();
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
        var features = new FeatureCollectionLookup();
        features.Set("test");
        features.Checkpoint();
        features.Set<string>(null);
        features.ResetCheckpoint();
        Assert.Equal("test", features.Get<string>());
    }

    [Fact]
    public void EmptyCheckpointString()
    {
        var features = new FeatureCollectionLookup();
        features.Checkpoint();
        features.Set("test");
        features.ResetCheckpoint();
        Assert.Null(features.Get<string>());
    }

    [Fact]
    public void NullableValueType()
    {
        DateTime? testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollectionLookup();
        features.Set(testDate);
        features.Checkpoint();
        Assert.Equal(testDate, features.Get<DateTime?>());
        features.Set<DateTime?>(null);
        Assert.Null(features.Get<DateTime?>());
        features.ResetCheckpoint();
        Assert.Equal(testDate, features.Get<DateTime?>());
    }

    [Fact]
    public void EnumerateFeatures()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollectionLookup();
        Assert.True(features.SequenceEqual([]));
        features.Set(testDate);
        features.Checkpoint();
        features.Set("test");
        Assert.True(features.SequenceEqual([new(typeof(DateTime), testDate), new(typeof(string), "test")]));
        features.ResetCheckpoint();
        Assert.True(features.SequenceEqual([new(typeof(DateTime), testDate)]));
    }

    [Fact]
    public void Copy()
    {
        var testDate = new DateTime(2025, 06, 24);
        var features = new FeatureCollectionLookup();
        features.Set(testDate);
        features.Set("test");
        var copy = features.Copy();
        features.Set<string>(null);
        features.Set<DateTime>(default);

        Assert.True(copy.SequenceEqual([new(typeof(DateTime), testDate), new(typeof(string), "test")]));
        copy.ResetCheckpoint();
        Assert.True(copy.SequenceEqual([]));
    }
}
