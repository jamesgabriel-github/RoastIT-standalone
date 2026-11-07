using RoastIT.App.Services;

namespace RoastIT.Tests.App;

public class BackNavigationServiceTests
{
    [Fact]
    public async Task With_no_layer_open_back_is_not_handled()
    {
        var back = new BackNavigationService();

        Assert.False(await back.TryCloseTopAsync());
        Assert.False(back.HasLayers);
    }

    [Fact]
    public async Task Back_closes_only_the_top_layer()
    {
        var back = new BackNavigationService();
        var closed = new List<string>();
        using var full = back.Push(LayerKind.FullSheet, () => { closed.Add("full"); return Task.CompletedTask; });
        using var bottom = back.Push(LayerKind.BottomSheet, () => { closed.Add("bottom"); return Task.CompletedTask; });

        Assert.True(await back.TryCloseTopAsync());

        Assert.Equal(["bottom"], closed);
    }

    [Fact]
    public async Task Layers_close_in_the_order_pin_prompt_dialog_bottom_sheet_full_sheet_whatever_the_open_order()
    {
        var back = new BackNavigationService();
        var closed = new List<string>();
        // Opened in a deliberately scrambled order.
        var registrations = new[]
        {
            back.Push(LayerKind.BottomSheet, () => { closed.Add("bottom"); return Task.CompletedTask; }),
            back.Push(LayerKind.PinPrompt, () => { closed.Add("pin"); return Task.CompletedTask; }),
            back.Push(LayerKind.FullSheet, () => { closed.Add("full"); return Task.CompletedTask; }),
            back.Push(LayerKind.Dialog, () => { closed.Add("dialog"); return Task.CompletedTask; }),
        };

        // Each close callback stands in for the component closing itself (disposing its registration).
        var order = new List<string>();
        while (back.HasLayers)
        {
            closed.Clear();
            await back.TryCloseTopAsync();
            order.AddRange(closed);
            // Dispose the registration matching what just closed.
            var index = closed[0] switch { "pin" => 1, "dialog" => 3, "bottom" => 0, _ => 2 };
            registrations[index].Dispose();
        }

        Assert.Equal(["pin", "dialog", "bottom", "full"], order);
    }

    [Fact]
    public async Task Among_layers_of_the_same_kind_the_most_recent_closes_first()
    {
        var back = new BackNavigationService();
        var closed = new List<string>();
        using var first = back.Push(LayerKind.FullSheet, () => { closed.Add("first"); return Task.CompletedTask; });
        using var second = back.Push(LayerKind.FullSheet, () => { closed.Add("second"); return Task.CompletedTask; });

        await back.TryCloseTopAsync();

        Assert.Equal(["second"], closed);
    }

    [Fact]
    public void Disposing_a_registration_removes_the_layer_and_raises_the_change_event_once()
    {
        var back = new BackNavigationService();
        var changes = 0;
        back.LayersChanged += () => changes++;

        var registration = back.Push(LayerKind.Dialog, () => Task.CompletedTask);
        registration.Dispose();
        registration.Dispose(); // second dispose is a no-op

        Assert.False(back.HasLayers);
        Assert.Equal(2, changes); // one for open, one for close
    }

    [Fact]
    public async Task A_pages_own_back_only_runs_when_nothing_is_open_over_it()
    {
        var back = new BackNavigationService();
        var closed = new List<string>();
        using var page = back.Push(LayerKind.PageBack, () => { closed.Add("page"); return Task.CompletedTask; });
        using var sheet = back.Push(LayerKind.FullSheet, () => { closed.Add("sheet"); return Task.CompletedTask; });

        await back.TryCloseTopAsync();
        sheet.Dispose();
        await back.TryCloseTopAsync();

        Assert.Equal(["sheet", "page"], closed);
    }

    [Fact]
    public void A_pages_own_back_is_not_an_overlay_and_does_not_hide_toasts_or_the_fab()
    {
        var back = new BackNavigationService();

        using var page = back.Push(LayerKind.PageBack, () => Task.CompletedTask);

        Assert.True(back.HasLayers);
        Assert.False(back.HasOverlay);
        Assert.False(back.HasFullSheet);
        Assert.False(back.HasBlockingOverlay);
    }

    [Fact]
    public void Toasts_hide_under_bottom_sheets_dialogs_and_pin_prompts_but_not_full_sheets()
    {
        var back = new BackNavigationService();

        using (back.Push(LayerKind.FullSheet, () => Task.CompletedTask))
        {
            Assert.True(back.HasFullSheet);
            Assert.False(back.HasBlockingOverlay);
        }

        foreach (var kind in new[] { LayerKind.BottomSheet, LayerKind.Dialog, LayerKind.PinPrompt })
        {
            using (back.Push(kind, () => Task.CompletedTask))
            {
                Assert.True(back.HasBlockingOverlay);
            }
        }
    }
}
