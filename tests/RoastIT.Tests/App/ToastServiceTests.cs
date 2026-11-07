using RoastIT.App.Services;

namespace RoastIT.Tests.App;

public class ToastServiceTests
{
    [Fact]
    public async Task A_toast_disappears_after_its_duration()
    {
        var toasts = new ToastService();
        var hidden = new TaskCompletionSource();
        toasts.Changed += () => { if (toasts.Message is null) hidden.TrySetResult(); };

        toasts.Show("Saved", TimeSpan.FromMilliseconds(30));
        Assert.Equal("Saved", toasts.Message);

        await hidden.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(toasts.Message);
    }

    [Fact]
    public async Task A_new_toast_replaces_the_current_one_and_restarts_the_timer()
    {
        var toasts = new ToastService();

        toasts.Show("First", TimeSpan.FromMilliseconds(60));
        await Task.Delay(30);
        toasts.Show("Second", TimeSpan.FromMilliseconds(400));
        await Task.Delay(150); // past the first toast's original deadline

        Assert.Equal("Second", toasts.Message);
    }
}
