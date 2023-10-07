using System.Text;
using CHttp.Http;

namespace CHttp.Tests.Http;

public class UploadThrottledStringContentTests
{
	[Fact]
	public async Task SingleLoopWrite()
	{
		string input = "test";
		var awaiter = new SyncedAwaiter(1);
		var sut = new UploadThrottledStringContent(input, 1, awaiter);
		using var ms = new MemoryStream();
		await sut.CopyToAsync(ms);
		ms.Seek(0, SeekOrigin.Begin);
		Assert.Equal(Encoding.UTF8.GetBytes(input), ms.ToArray());
		Assert.Equal(1, awaiter.RemainingCount);
	}

	[Fact]
	public async Task TwoLoopWrites()
	{
		string input = new string('0', 16);
		var awaiter = new SyncedAwaiter(1);
		var sut = new UploadThrottledStringContent(input, 1, awaiter);
		using var ms = new MemoryStream();
		await sut.CopyToAsync(ms);
		ms.Seek(0, SeekOrigin.Begin);
		Assert.Equal(Encoding.UTF8.GetBytes(input), ms.ToArray());
		Assert.Equal(0, awaiter.RemainingCount);
	}

	[Fact]
	public async Task LargeContent()
	{
		string input = new string('0', 4501);
		var awaiter = new SyncedAwaiter(300);
		var sut = new UploadThrottledStringContent(input, 1, awaiter);
		using var ms = new MemoryStream();
		await sut.CopyToAsync(ms);
		ms.Seek(0, SeekOrigin.Begin);
		Assert.Equal(Encoding.UTF8.GetBytes(input), ms.ToArray());
		Assert.Equal(0, awaiter.RemainingCount);
	}
}
