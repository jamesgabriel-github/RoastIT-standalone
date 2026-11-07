using RoastIT.App.Services;
using RoastIT.Core.Enums;
using RoastIT.Core.Services;

namespace RoastIT.Tests.App;

public class PinPromptServiceTests : IDisposable
{
    private const string Pin = "4821";
    private readonly AppLockFixture _fx = new();
    private readonly AppLockService _lock;
    private readonly PinPromptService _prompts;

    public PinPromptServiceTests()
    {
        _lock = _fx.CreateService();
        _lock.SetUpAsync(Pin).GetAwaiter().GetResult();
        _prompts = new PinPromptService(_lock);
    }

    public void Dispose() => _fx.Dispose();

    private static async Task<T> Completes<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Requesting_opens_the_prompt_and_a_correct_pin_completes_it_with_a_confirmation()
    {
        var pending = _prompts.RequestAsync("Hide Liempo from new bookings.", "Deactivate", PinScope.Menu);

        Assert.Equal("Hide Liempo from new bookings.", _prompts.Current!.Summary);
        Assert.Equal("Deactivate", _prompts.Current.ConfirmLabel);

        var confirm = await _lock.ConfirmAsync(Pin, PinScope.Menu);
        _prompts.Complete(confirm.Confirmation!);
        var result = await Completes(pending);

        Assert.True(result.Confirmed);
        Assert.NotNull(result.Confirmation);
        Assert.Null(_prompts.Current);
        _lock.EnsureConfirmed(result.Confirmation, PinScope.Menu); // the service accepts it
    }

    [Fact]
    public async Task Cancelling_returns_not_confirmed_and_closes_the_prompt()
    {
        var pending = _prompts.RequestAsync("Add Crispy Pata to the menu.", "Add", PinScope.Menu);

        _prompts.Cancel();

        var result = await Completes(pending);
        Assert.False(result.Confirmed);
        Assert.Null(result.Confirmation);
        Assert.Null(_prompts.Current);
    }

    [Fact]
    public async Task A_bookings_request_inside_the_three_minute_window_skips_the_prompt()
    {
        await _lock.ConfirmAsync(Pin, PinScope.Bookings);

        var result = await Completes(_prompts.RequestAsync("Record a ₱500 payment.", "Record", PinScope.Bookings));

        Assert.True(result.Confirmed);
        Assert.Null(result.Confirmation); // allowed by the window; the service accepts null here
        Assert.Null(_prompts.Current);    // no prompt was shown
    }

    [Fact]
    public async Task A_bookings_request_after_the_window_ends_asks_again()
    {
        await _lock.ConfirmAsync(Pin, PinScope.Bookings);
        _fx.Clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));

        var pending = _prompts.RequestAsync("Record a ₱500 payment.", "Record", PinScope.Bookings);

        Assert.NotNull(_prompts.Current);
        _prompts.Cancel();
        Assert.False((await Completes(pending)).Confirmed);
    }

    [Fact]
    public async Task Menu_requests_always_ask_even_inside_a_bookings_window()
    {
        await _lock.ConfirmAsync(Pin, PinScope.Bookings);

        var pending = _prompts.RequestAsync("Save changes to Lechon Belly.", "Save", PinScope.Menu);

        Assert.NotNull(_prompts.Current);
        _prompts.Cancel();
        await Completes(pending);
    }

    [Fact]
    public async Task Locking_the_app_cancels_an_open_prompt()
    {
        var pending = _prompts.RequestAsync("Export all bookings…", "Export", PinScope.Menu);

        _lock.Lock();

        Assert.False((await Completes(pending)).Confirmed);
        Assert.Null(_prompts.Current);
    }

    [Fact]
    public async Task Nothing_can_be_requested_while_locked()
    {
        _lock.Lock();

        var result = await Completes(_prompts.RequestAsync("Add Crispy Pata…", "Add", PinScope.Menu));

        Assert.False(result.Confirmed);
        Assert.Null(_prompts.Current);
    }

    [Fact]
    public async Task Only_one_prompt_is_open_at_a_time()
    {
        var first = _prompts.RequestAsync("First", "OK", PinScope.Menu);

        var second = await Completes(_prompts.RequestAsync("Second", "OK", PinScope.Menu));

        Assert.False(second.Confirmed);
        Assert.Equal("First", _prompts.Current!.Summary);
        _prompts.Cancel();
        await Completes(first);
    }

    [Fact]
    public async Task The_changed_event_fires_when_a_prompt_opens_and_when_it_closes()
    {
        var changes = 0;
        _prompts.Changed += () => changes++;

        var pending = _prompts.RequestAsync("Summary", "OK", PinScope.Menu);
        _prompts.Cancel();
        await Completes(pending);

        Assert.Equal(2, changes);
    }
}
