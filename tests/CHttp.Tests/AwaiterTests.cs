using CHttp.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CHttp.Tests;

public class AwaiterTests
{
	[Fact]
	public async Task WaitAsync_Waits50Ms()
	{
		var timeProvider = new FakeTimeProvider();
		var sut = new Awaiter(timeProvider);
		var waiting = sut.WaitAsync(TimeSpan.FromMilliseconds(50));
		timeProvider.Advance(TimeSpan.FromMilliseconds(49));
		Assert.False(waiting.IsCompleted);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		await waiting.WaitAsync(TimeSpan.FromSeconds(1));
	}
}
