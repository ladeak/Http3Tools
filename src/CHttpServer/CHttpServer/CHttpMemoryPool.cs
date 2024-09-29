using System.Buffers;
using Microsoft.AspNetCore.Connections.Features;

namespace CHttpServer;

public class CHttpMemoryPool : IMemoryPoolFeature
{
    public MemoryPool<byte> MemoryPool { get; } = MemoryPool<byte>.Shared;
}
