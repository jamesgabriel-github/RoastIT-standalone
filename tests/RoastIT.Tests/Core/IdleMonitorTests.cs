using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

public class IdleMonitorTests : IDisposable
{
    private readonly AppLockFixture _fx = new();
    private readonly AppLockService _lock;
    private readonly IdleMonitor _idle;

    public IdleMonitorTests()
    {
        _lock = _fx.CreateService();
        _lock.SetUpAsync("4821").GetAwaiter().GetResult();
        _idle = new IdleMonitor(_fx.Clock, _lock);
    }

    public void Dispose()
    {
        _idle.Dispose();
        _fx.Dispose();
    }

    [Fact]
    public void The_app_locks_after_ten_minutes_without_activity()
    {
        _idle.NotifyActivity();
        _fx.Clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        Assert.False(_idle.CheckAndLock());
        Assert.False(_lock.IsLocked);

        _fx.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(_idle.CheckAndLock());
        Assert.True(_lock.IsLocked);
    }

    [Fact]
    public void Activity_restarts_the_ten_minutes()
    {
        _idle.NotifyActivity();
        _fx.Clock.Advance(TimeSpan.FromMinutes(8));
        _idle.NotifyActivity();
        _fx.Clock.Advance(TimeSpan.FromMinutes(8));

        Assert.False(_idle.CheckAndLock());
        Assert.False(_lock.IsLocked);
    }

    [Fact]
    public async Task Unlocking_starts_a_fresh_idle_period()
    {
        _lock.Lock();
        _fx.Clock.Advance(TimeSpan.FromMinutes(30)); // sat on the lock screen

        await _lock.UnlockAsync("4821");

        Assert.False(_idle.CheckAndLock());
        Assert.False(_lock.IsLocked);
    }

    [Fact]
    public void Checking_while_already_locked_does_nothing()
    {
        _lock.Lock();
        _fx.Clock.Advance(TimeSpan.FromHours(1));

        Assert.False(_idle.CheckAndLock());
    }
}
