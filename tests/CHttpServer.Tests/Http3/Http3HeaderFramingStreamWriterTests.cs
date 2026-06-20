using System.Buffers;
using System.Net.Quic;
using System.Runtime.InteropServices;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class Http3HeaderFramingStreamWriterTests
{
    [Theory]
    [InlineData(10, 2)]
    [InlineData(63, 2)]
    [InlineData(64, 3)]
    [InlineData(100, 3)]
    [InlineData(20000, 5)]
    public async Task GetSpan_Flush_WritesToStreamWithFrameHeader(int payloadLength, int headerLength)
    {
        var stream = new MemoryStream();
        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        var buffer = sut.GetSpan(payloadLength);
        Assert.True(buffer.Length >= payloadLength);
        for (int i = 0; i < payloadLength; i++)
            buffer[i] = (byte)i;

        sut.Advance(payloadLength);
        await sut.FlushAsync(TestContext.Current.CancellationToken);

        var result = stream.ToArray();
        Assert.Equal(headerLength + payloadLength, result.Length);
        Assert.Equal(1, result[0]);
        VariableLenghtIntegerDecoder.TryRead(result.AsSpan(1), out var writtenPayloadLength, out var writtenBytesCount);
        Assert.Equal(payloadLength, (int)writtenPayloadLength);
        Assert.Equal(headerLength - 1, writtenBytesCount);

        for (int i = 0; i < payloadLength; i++)
            Assert.Equal((byte)i, result[i + headerLength]);

        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public void GetMemory_NoAdvance_Returns_SameMemory()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null);
        var m1 = sut.GetMemory(100);
        m1.Span.Fill(1);

        var m2 = sut.GetMemory(50);
        m2.Span.SequenceEqual(m1.Span[0..50]);

        var m3 = sut.GetSpan(100);
        m3.SequenceEqual(m1.Span[0..50]);

        var m4 = sut.GetSpan(50);
        m4.SequenceEqual(m3[0..50]);
    }

    [Fact]
    public void GetMemory_Advance_Returns_NotSameMemory()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null);
        var m1 = sut.GetMemory(100);
        m1.Span.Fill(1);
        sut.Advance(100);

        var m2 = sut.GetMemory(50);
        m2.Span.SequenceEqual(new byte[100]);
        sut.Advance(50);

        var m3 = sut.GetSpan(100);
        m3.Fill(1);
        sut.Advance(100);

        var m4 = sut.GetSpan(50);
        m2.Span.SequenceEqual(new byte[100]);
    }

    [Fact]
    public void AdvanceTooMuch_ThrowsArgumentOutOfRangeException()
    {
        var smallArrayPool = ArrayPool<byte>.Create(128, 1);
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null, smallArrayPool);
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.Advance(4097));

        sut.GetMemory(4096);
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.Advance(4097));

        // Should not throw
        sut.Advance(128);
    }

    [Fact]
    public async Task WhenCompleted_CannotGetMoreMemory()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null);
        sut.Complete();
        sut.Complete(); // It is OK to call twice.
        Assert.Throws<InvalidOperationException>(() => sut.GetMemory(1));
        Assert.Throws<InvalidOperationException>(() => sut.GetSpan(1));
        Assert.Throws<InvalidOperationException>(() => sut.Advance(1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.WriteAsync(new byte[1], TestContext.Current.CancellationToken));

        // Flush does not throw, because Completion flushed all.
        await sut.FlushAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenCompletedAsync_CannotGetMoreMemory()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null);
        await sut.CompleteAsync();
        await sut.CompleteAsync(); // It is OK to call twice.
        Assert.Throws<InvalidOperationException>(() => sut.GetMemory(1));
        Assert.Throws<InvalidOperationException>(() => sut.GetSpan(1));
        Assert.Throws<InvalidOperationException>(() => sut.Advance(1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.WriteAsync(new byte[1], TestContext.Current.CancellationToken));

        // Flush does not throw, because Completion flushed all.
        await sut.FlushAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WriteAsync_WritesToStreamAndFlushes()
    {
        var stream = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        byte[] data = [0, 1, 2, 3, 4, 5];
        await sut.WriteAsync(data, TestContext.Current.CancellationToken);

        int headerLength = 2;
        int payloadLength = data.Length;
        var result = stream.ToArray();
        Assert.Equal(headerLength + payloadLength, result.Length);
        Assert.Equal(1, result[0]);
        VariableLenghtIntegerDecoder.TryRead(result.AsSpan(1), out var writtenPayloadLength, out var writtenBytesCount);
        Assert.Equal(payloadLength, (int)writtenPayloadLength);
        Assert.Equal(headerLength - 1, writtenBytesCount);

        for (int i = 0; i < payloadLength; i++)
            Assert.Equal((byte)i, result[i + headerLength]);
    }

    [Fact]
    public async Task WriteAsync_GetMemory_DoesNotMix()
    {
        var stream = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        sut.GetMemory(1);
        sut.Advance(1);

        byte[] data = [0, 1, 2, 3, 4, 5];
        await sut.WriteAsync(data, TestContext.Current.CancellationToken);

        int headerLength = 2;
        int payloadLength = data.Length;
        var result = stream.ToArray();
        Assert.Equal(headerLength + payloadLength, result.Length);
        Assert.Equal(1, result[0]);
        VariableLenghtIntegerDecoder.TryRead(result.AsSpan(1), out var writtenPayloadLength, out var writtenBytesCount);
        Assert.Equal(payloadLength, (int)writtenPayloadLength);
        Assert.Equal(headerLength - 1, writtenBytesCount);

        for (int i = 0; i < payloadLength; i++)
            Assert.Equal((byte)i, result[i + headerLength]);
    }

    [Fact]
    public async Task Reset_Clears_Buffers()
    {
        var stream = new MemoryStream();
        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        var memory = sut.GetSpan(1);
        memory[0] = 1;
        sut.Advance(1);
        Assert.Equal(1, sut.UnflushedBytes);
        sut.Reset(stream);
        await sut.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, stream.Position);
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public void Complete_FlushesBuffer()
    {
        var stream = new MemoryStream();
        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        var memory = sut.GetSpan(1);
        memory[0] = 1;
        sut.Advance(1);
        Assert.Equal(1, sut.UnflushedBytes);
        sut.Complete();

        Assert.Equal(3, stream.Position);
        Assert.Equal(0, sut.UnflushedBytes);
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task CompleteAsync_FlushesBuffer()
    {
        var stream = new MemoryStream();
        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        var memory = sut.GetSpan(1);
        memory[0] = 1;
        sut.Advance(1);
        Assert.Equal(1, sut.UnflushedBytes);
        await sut.CompleteAsync();

        Assert.Equal(3, stream.Position);
        Assert.Equal(0, sut.UnflushedBytes);
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public void MultipleAdvanceCalls()
    {
        var stream = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        var memory = sut.GetSpan(5);
        memory[0] = 0;
        memory[1] = 1;
        sut.Advance(1);
        sut.Advance(1);
        Assert.Equal(2, sut.UnflushedBytes);
        memory = sut.GetSpan(2);
        memory[0] = 2;
        memory[1] = 3;
        sut.Advance(2);

        Assert.Equal(4, sut.UnflushedBytes);
        sut.Complete();

        Assert.Equal(6, stream.Position);
        byte[] expected = [1, 4, 0, 1, 2, 3];
        Assert.True(stream.ToArray().SequenceEqual(expected));
        Assert.Equal(0, sut.UnflushedBytes);
    }

    [Fact]
    public async Task CancelPendingFlush()
    {
        var stream = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        Assert.Throws<PlatformNotSupportedException>(() => sut.CancelPendingFlush());
    }

    [Fact]
    public async Task Flush_CancellationToken_Throws()
    {
        var stream = new MemoryStream();
        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(stream);
        var memory = sut.GetSpan(5);
        sut.Advance(5);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await sut.FlushAsync(new CancellationToken(true)));

        Assert.Equal(0, sut.UnflushedBytes);
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task Flushing_Into_ClosedStream()
    {
        // Setup a stream.
        var fixture = await QuicConnectionFixture.SetupConnectionAsync(6004, TestContext.Current.CancellationToken);
        var serverStreamAccepting = fixture.ServerConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken);
        var clientStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        clientStream.Write(new byte[1]);
        var testStream = await serverStreamAccepting;

        testStream.Close();

        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(testStream);
        var memory = sut.GetSpan(5);
        sut.Advance(5);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sut.FlushAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, sut.UnflushedBytes);
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task WriteAsync_Into_ClosedStream()
    {
        // Setup a stream.
        var fixture = await QuicConnectionFixture.SetupConnectionAsync(6003, TestContext.Current.CancellationToken);
        var serverStreamAccepting = fixture.ServerConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken);
        var clientStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        clientStream.Write(new byte[1]);
        var testStream = await serverStreamAccepting;

        testStream.Close();

        var arrayPool = new TestArrayPool();
        var sut = new Http3HeaderFramingStreamWriter(testStream);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sut.WriteAsync(new byte[1], TestContext.Current.CancellationToken));

        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task GetMemory_WhenNoDataAvailable_InSegment()
    {
        var ms = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(ms);
        Span<byte> data = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var memory0 = sut.GetMemory(data.Length);
        data.CopyTo(memory0.Span);
        sut.Advance(data.Length);

        var memory1 = sut.GetMemory(data.Length);
        data.CopyTo(memory1.Span);
        sut.Advance(data.Length);

        await sut.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(24, ms.Length);
    }

    [Fact]
    public async Task GetMemory_WhenDataAvailable_InSegment()
    {
        // Setup a stream.
        var ms = new MemoryStream();
        var arrayPool = new TestArrayPool(32, 2);
        var sut = new Http3HeaderFramingStreamWriter(ms);
        Span<byte> data = [0, 1, 2, 3, 4];
        var memory0 = sut.GetMemory(data.Length);
        data.CopyTo(memory0.Span);
        sut.Advance(data.Length);

        var memory1 = sut.GetMemory(data.Length);
        data.CopyTo(memory1.Span);
        sut.Advance(data.Length);

        await sut.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sut.UnflushedBytes);
        data = [1, 10, 0, 1, 2, 3, 4, 0, 1, 2, 3, 4];
        Assert.True(ms.ToArray().SequenceEqual(data));

        // Asserting that it is reusing the same array.
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory0, out var segment0));
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory1, out var segment1));
        Assert.Same(segment0.Array, segment1.Array);

        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task GetMemory_AddsNewSegments()
    {
        // Setup a stream.
        var ms = new MemoryStream();
        var arrayPool = new TestArrayPool(16, 2);
        var sut = new Http3HeaderFramingStreamWriter(ms);
        byte[] data = Enumerable.Sequence(0, 2048, 1).Select(x => (byte)x).ToArray();
        var memory0 = sut.GetMemory(data.Length);
        data.CopyTo(memory0.Span);
        sut.Advance(data.Length);

        var memory1 = sut.GetMemory(data.Length);
        data.CopyTo(memory1.Span);
        sut.Advance(data.Length);

        await sut.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sut.UnflushedBytes);
        Assert.Equal(3 + 2 * data.Length, ms.ToArray().Length);

        // Asserting that it is using a new array segment.
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory0, out var segment0));
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory1, out var segment1));
        Assert.NotSame(segment0.Array, segment1.Array);

        // After Flush, no outstanding bytes.
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task GetMemory_ReturnsArrayThatWasUnusedIfTooSmall()
    {
        // Setup a stream.
        var ms = new MemoryStream();
        var arrayPool = new TestArrayPool(32, 2);
        var sut = new Http3HeaderFramingStreamWriter(ms);
        byte[] data = Enumerable.Sequence(0, 2049, 1).Select(x => (byte)x).ToArray();
        var memory0 = sut.GetMemory(data.Length);
        data.CopyTo(memory0.Span);
        // No Advance

        var memory1 = sut.GetMemory(data.Length * 2);
        data.CopyTo(memory1.Span);
        sut.Advance(data.Length);

        await sut.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sut.UnflushedBytes);
        Assert.Equal(3 + data.Length, ms.ToArray().Length);

        // Asserting that it is using a new array segment.
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory0, out var segment0));
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory1, out var segment1));
        Assert.NotSame(segment0.Array, segment1.Array);

        // After Flush, no outstanding bytes.
        Assert.Equal(0, arrayPool.OutstandingBytes);
    }

    [Fact]
    public async Task GetMemoryFlushAsync_Writes_FrameType()
    {
        var ms = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(ms);
        sut.GetMemory(1).Span[0] = 9;
        sut.Advance(1);
        await sut.FlushAsync(TestContext.Current.CancellationToken);

        byte[] expected = [1, 99, 9];
        VariableLenghtIntegerDecoder.TryWrite(expected.AsSpan(1), 1, out _);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public void Complete_Writes_FrameType()
    {
        byte frameType = 1;
        var ms = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(ms);
        sut.GetMemory(1).Span[0] = 9;
        sut.Advance(1);
        sut.Complete();

        byte[] expected = [frameType, 99, 9];
        VariableLenghtIntegerDecoder.TryWrite(expected.AsSpan(1), 1, out _);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public async Task CompleteAsync_Writes_FrameType()
    {
        byte frameType = 1;
        var ms = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(ms);
        sut.GetMemory(1).Span[0] = 9;
        sut.Advance(1);
        await sut.CompleteAsync();

        byte[] expected = [frameType, 99, 9];
        VariableLenghtIntegerDecoder.TryWrite(expected.AsSpan(1), 1, out _);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public async Task WriteAsync_Writes_FrameType()
    {
        byte frameType = 1;
        var ms = new MemoryStream();
        var sut = new Http3HeaderFramingStreamWriter(ms);
        byte[] data = [7];
        await sut.WriteAsync(data, TestContext.Current.CancellationToken);

        byte[] expected = [frameType, 99, 7];
        VariableLenghtIntegerDecoder.TryWrite(expected.AsSpan(1), 1, out _);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public async Task ArrayPool_DoubleReturn_FlushAsync_Complete()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null, new TestArrayPool());
        sut.GetMemory(8192);
        sut.Advance(8100);
        sut.GetMemory(8192);
        sut.Advance(8100);
        // Flushes 2 segments.
        await sut.FlushAsync(TestContext.Current.CancellationToken);

        sut.Complete(); // Should not double clear segments. If so, TestArrayPool will throw an exception.
    }

    [Fact]
    public void ArrayPool_DoubleReturn_Flush_Complete()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null, new TestArrayPool());
        sut.GetMemory(8192);
        sut.Advance(8100);
        sut.GetMemory(8192);
        sut.Advance(8100);
        // Flushes 2 segments.
        sut.Flush();

        sut.Complete(); // Should not double clear segments. If so, TestArrayPool will throw an exception.
    }

    [Fact]
    public async Task ArrayPool_DoubleReturn_FlushAsync_CompleteAsync()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null, new TestArrayPool());
        sut.GetMemory(8192);
        sut.Advance(8100);
        sut.GetMemory(8192);
        sut.Advance(8100);
        // Flushes 2 segments.
        await sut.FlushAsync(TestContext.Current.CancellationToken);
        
        await sut.CompleteAsync(); // Should not double clear segments. If so, TestArrayPool will throw an exception.
    }

    [Fact]
    public async Task ArrayPool_DoubleReturn_Flush_CompleteAsync()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null, new TestArrayPool());
        sut.GetMemory(8192);
        sut.Advance(8100);
        sut.GetMemory(8192);
        sut.Advance(8100);
        // Flushes 2 segments.
        sut.Flush();
        
        await sut.CompleteAsync(); // Should not double clear segments. If so, TestArrayPool will throw an exception.
    }

    [Fact]
    public void ArrayPool_DoubleReturn_Flush_Reset()
    {
        var sut = new Http3HeaderFramingStreamWriter(Stream.Null, new TestArrayPool());
        sut.GetMemory(8192);
        sut.Advance(8100);
        sut.GetMemory(8192);
        sut.Advance(8100);
        // Flushes 2 segments.
        sut.Flush();

        sut.Reset(Stream.Null); // Should not double clear segments. If so, TestArrayPool will throw an exception.
    }

    private class TestArrayPool : ArrayPool<byte>
    {
        private readonly ArrayPool<byte> _internalPool;
        private readonly List<byte[]> _rentedArrays = new();

        public TestArrayPool()
        {
            _internalPool = Shared;
        }

        public TestArrayPool(int maxArrayLength, int maxArrayPerBucket)
        {
            _internalPool = Create(maxArrayLength, maxArrayPerBucket);
        }

        public int OutstandingBytes => _rentedArrays.Sum(x => x.Length);

        public override byte[] Rent(int minimumLength)
        {
            var buffer = _internalPool.Rent(minimumLength);
            _rentedArrays.Add(buffer);
            return buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            _internalPool.Return(array, clearArray);
            Assert.True(_rentedArrays.Remove(array));
        }
    }
}

