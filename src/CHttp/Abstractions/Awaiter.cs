namespace CHttp.Abstractions;

internal sealed class Awaiter : IAwaiter
{
	private readonly TimeProvider _timeProvider;

	public Awaiter()
	{
		_timeProvider = TimeProvider.System;
	}

	public Awaiter(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public Task WaitAsync(TimeSpan duration) => Task.Delay(duration, _timeProvider);
}
