using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

public class AppLockServiceTests : IDisposable
{
    private const string Pin = "4821";
    private readonly AppLockFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private async Task<(AppLockService Lock, string Code)> SetUpAsync(string pin = Pin)
    {
        var svc = _fx.CreateService();
        var code = await svc.SetUpAsync(pin);
        return (svc, code);
    }

    // ---- Setup ------------------------------------------------------------------

    [Fact]
    public async Task The_app_starts_locked_and_a_fresh_install_is_not_set_up()
    {
        var svc = _fx.CreateService();

        Assert.True(svc.IsLocked);
        Assert.False(await svc.IsSetUpAsync());
        Assert.Throws<AccessDeniedException>(svc.EnsureUnlocked);
    }

    [Fact]
    public async Task Setup_unlocks_and_returns_a_formatted_recovery_code_once()
    {
        var svc = _fx.CreateService();

        var code = await svc.SetUpAsync(Pin);

        Assert.False(svc.IsLocked);
        Assert.True(await svc.IsSetUpAsync());
        Assert.Matches("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", code);
    }

    [Fact]
    public async Task Only_hashes_are_stored_never_the_pin_or_the_recovery_code()
    {
        var (_, code) = await SetUpAsync();

        await using var db = _fx.Factory.CreateDbContext();
        var row = await db.AppLock.AsNoTracking().SingleAsync();

        Assert.DoesNotContain(Pin, row.PinHash);
        Assert.NotEqual(Pin, row.PinHash);
        Assert.DoesNotContain(code.Replace("-", ""), row.RecoveryCodeHash);
        Assert.NotEqual(code, row.RecoveryCodeHash);
    }

    [Theory]
    [InlineData("123")]       // too short
    [InlineData("1234567")]   // too long
    [InlineData("12a4")]      // not digits
    [InlineData("")]
    public async Task Setup_rejects_pins_that_are_not_4_to_6_digits(string pin)
    {
        var svc = _fx.CreateService();

        await Assert.ThrowsAsync<ValidationException>(() => svc.SetUpAsync(pin));
        Assert.False(await svc.IsSetUpAsync());
    }

    [Fact]
    public async Task Setup_cannot_overwrite_an_existing_pin()
    {
        await SetUpAsync();
        var other = _fx.CreateService();

        await Assert.ThrowsAsync<AccessDeniedException>(() => other.SetUpAsync("999999"));

        Assert.True((await other.UnlockAsync(Pin)).Succeeded); // original PIN still works
    }

    // ---- Unlock and lockout -------------------------------------------------------

    [Fact]
    public async Task Unlock_with_the_right_pin_unlocks_and_raises_the_change_event()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();
        var changes = 0;
        svc.LockStateChanged += () => changes++;

        var attempt = await svc.UnlockAsync(Pin);

        Assert.True(attempt.Succeeded);
        Assert.False(svc.IsLocked);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_wrong_pin_reports_tries_left_and_stays_locked()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();

        var first = await svc.UnlockAsync("0000");
        var second = await svc.UnlockAsync("0000");

        Assert.Equal(PinAttemptStatus.Wrong, first.Status);
        Assert.Equal(4, first.TriesLeft);
        Assert.Equal(3, second.TriesLeft);
        Assert.True(svc.IsLocked);
    }

    [Fact]
    public async Task Five_wrong_tries_lock_input_for_30_seconds_even_for_the_right_pin()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();

        PinAttempt last = default!;
        for (var i = 0; i < 5; i++) last = await svc.UnlockAsync("0000");

        Assert.Equal(PinAttemptStatus.LockedOut, last.Status);
        Assert.Equal(30, last.SecondsLeft(_fx.Clock.UtcNow));

        var duringLockout = await svc.UnlockAsync(Pin);
        Assert.Equal(PinAttemptStatus.LockedOut, duringLockout.Status);
        Assert.True(svc.IsLocked);

        _fx.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(PinAttemptStatus.LockedOut, (await svc.UnlockAsync(Pin)).Status);

        _fx.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await svc.UnlockAsync(Pin)).Succeeded);
    }

    [Fact]
    public async Task After_a_lockout_the_counter_starts_again_from_zero()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();
        for (var i = 0; i < 5; i++) await svc.UnlockAsync("0000");
        _fx.Clock.Advance(TimeSpan.FromSeconds(31));

        var attempt = await svc.UnlockAsync("0000");

        Assert.Equal(PinAttemptStatus.Wrong, attempt.Status);
        Assert.Equal(4, attempt.TriesLeft);
    }

    [Fact]
    public async Task Tries_made_while_locked_out_do_not_extend_the_lockout()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();
        for (var i = 0; i < 5; i++) await svc.UnlockAsync("0000");

        _fx.Clock.Advance(TimeSpan.FromSeconds(20));
        await svc.UnlockAsync("0000");
        await svc.UnlockAsync("0000");
        _fx.Clock.Advance(TimeSpan.FromSeconds(10));

        Assert.True((await svc.UnlockAsync(Pin)).Succeeded);
    }

    [Fact]
    public async Task The_lockout_survives_an_app_restart()
    {
        await SetUpAsync();
        var before = _fx.CreateService();
        for (var i = 0; i < 5; i++) await before.UnlockAsync("0000");

        var afterRestart = _fx.CreateService();
        var status = await afterRestart.GetLockoutAsync();
        var attempt = await afterRestart.UnlockAsync(Pin);

        Assert.True(status.IsLockedOut);
        Assert.Equal(PinAttemptStatus.LockedOut, attempt.Status);
        Assert.True(afterRestart.IsLocked);
    }

    [Fact]
    public async Task Failed_tries_survive_an_app_restart_and_a_right_pin_resets_them()
    {
        await SetUpAsync();
        var before = _fx.CreateService();
        await before.UnlockAsync("0000");
        await before.UnlockAsync("0000");

        var afterRestart = _fx.CreateService();
        Assert.Equal(3, (await afterRestart.GetLockoutAsync()).TriesLeft);

        Assert.True((await afterRestart.UnlockAsync(Pin)).Succeeded);
        Assert.Equal(5, (await afterRestart.GetLockoutAsync()).TriesLeft);
    }

    [Fact]
    public async Task Locked_until_is_read_back_as_utc()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();
        for (var i = 0; i < 5; i++) await svc.UnlockAsync("0000");

        var status = await svc.GetLockoutAsync();

        Assert.Equal(DateTimeKind.Utc, status.LockedUntil!.Value.Kind);
        Assert.Equal(_fx.Clock.UtcNow.AddSeconds(30), status.LockedUntil);
    }

    // ---- Change PIN -------------------------------------------------------------

    [Fact]
    public async Task Change_pin_swaps_the_pin_keeps_the_recovery_code_and_writes_an_audit_row()
    {
        var (svc, code) = await SetUpAsync();

        var attempt = await svc.ChangeAsync(Pin, "246810");

        Assert.True(attempt.Succeeded);
        var fresh = _fx.CreateService();
        Assert.Equal(PinAttemptStatus.Wrong, (await fresh.UnlockAsync(Pin)).Status);
        Assert.True((await fresh.UnlockAsync("246810")).Succeeded);
        Assert.True((await fresh.VerifyRecoveryCodeAsync(code)).Succeeded); // recovery code unchanged

        await using var db = _fx.Factory.CreateDbContext();
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Equal("pin.changed", audit.Action);
        Assert.Null(audit.Details);
    }

    [Fact]
    public async Task Change_pin_with_a_wrong_current_pin_changes_nothing_and_counts_toward_lockout()
    {
        var (svc, _) = await SetUpAsync();

        var attempt = await svc.ChangeAsync("0000", "246810");

        Assert.Equal(PinAttemptStatus.Wrong, attempt.Status);
        Assert.Equal(4, attempt.TriesLeft);
        Assert.True((await _fx.CreateService().UnlockAsync(Pin)).Succeeded);
        await using var db = _fx.Factory.CreateDbContext();
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Change_pin_validates_the_new_pin_before_counting_a_try()
    {
        var (svc, _) = await SetUpAsync();

        await Assert.ThrowsAsync<ValidationException>(() => svc.ChangeAsync("0000", "12"));

        Assert.Equal(5, (await svc.GetLockoutAsync()).TriesLeft);
    }

    [Fact]
    public async Task Change_pin_is_refused_while_locked()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();

        await Assert.ThrowsAsync<AccessDeniedException>(() => svc.ChangeAsync(Pin, "246810"));
    }

    // ---- Recovery code ----------------------------------------------------------

    [Fact]
    public async Task The_recovery_code_resets_the_pin_issues_a_new_code_and_the_old_code_stops_working()
    {
        var (_, oldCode) = await SetUpAsync();
        var svc = _fx.CreateService();

        // Lower case and spaces are fine (comparison ignores case, spaces and dashes).
        var messy = oldCode.ToLowerInvariant().Replace('-', ' ');
        Assert.True((await svc.VerifyRecoveryCodeAsync(messy)).Succeeded);
        var reset = await svc.ResetWithRecoveryCodeAsync(messy, "135790");

        Assert.True(reset.Attempt.Succeeded);
        Assert.NotNull(reset.NewRecoveryCode);
        Assert.NotEqual(oldCode, reset.NewRecoveryCode);
        Assert.False(svc.IsLocked);

        var fresh = _fx.CreateService();
        Assert.True((await fresh.UnlockAsync("135790")).Succeeded);
        Assert.Equal(PinAttemptStatus.Wrong, (await fresh.VerifyRecoveryCodeAsync(oldCode)).Status);
        Assert.True((await fresh.VerifyRecoveryCodeAsync(reset.NewRecoveryCode!)).Succeeded);

        await using var db = _fx.Factory.CreateDbContext();
        Assert.Equal("pin.reset_with_recovery", (await db.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task A_wrong_recovery_code_resets_nothing()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();

        var reset = await svc.ResetWithRecoveryCodeAsync("AAAA-BBBB-CCCC", "135790");

        Assert.Equal(PinAttemptStatus.Wrong, reset.Attempt.Status);
        Assert.Null(reset.NewRecoveryCode);
        Assert.True(svc.IsLocked);
        Assert.True((await _fx.CreateService().UnlockAsync(Pin)).Succeeded);
    }

    [Fact]
    public async Task Wrong_pins_and_wrong_recovery_codes_share_one_lockout_counter()
    {
        await SetUpAsync();
        var svc = _fx.CreateService();

        await svc.UnlockAsync("0000");
        await svc.VerifyRecoveryCodeAsync("AAAA-BBBB-CCCC");
        await svc.UnlockAsync("0000");
        await svc.VerifyRecoveryCodeAsync("AAAA-BBBB-CCCC");
        var fifth = await svc.UnlockAsync("0000");

        Assert.Equal(PinAttemptStatus.LockedOut, fifth.Status);
    }

    // ---- PIN confirmations (plan §9.5) --------------------------------------------

    [Fact]
    public async Task A_wrong_pin_in_a_prompt_counts_toward_the_same_lockout_as_the_lock_screen()
    {
        var (svc, _) = await SetUpAsync();

        var first = await svc.ConfirmAsync("0000", PinScope.Menu);
        var second = await svc.UnlockAsync("0000"); // the lock-screen path shares the counter

        Assert.Null(first.Confirmation);
        Assert.Equal(4, first.Attempt.TriesLeft);
        Assert.Equal(3, second.TriesLeft);
    }

    [Fact]
    public async Task A_menu_confirmation_is_single_use()
    {
        var (svc, _) = await SetUpAsync();
        var result = await svc.ConfirmAsync(Pin, PinScope.Menu);

        svc.EnsureConfirmed(result.Confirmation, PinScope.Menu); // first use passes

        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(result.Confirmation, PinScope.Menu));
    }

    [Fact]
    public async Task A_menu_confirmation_expires_after_about_a_minute()
    {
        var (svc, _) = await SetUpAsync();
        var result = await svc.ConfirmAsync(Pin, PinScope.Menu);

        _fx.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(result.Confirmation, PinScope.Menu));
    }

    [Fact]
    public async Task A_menu_action_needs_a_confirmation_and_a_bookings_confirmation_does_not_count_for_it()
    {
        var (svc, _) = await SetUpAsync();
        var bookings = await svc.ConfirmAsync(Pin, PinScope.Bookings);

        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(null, PinScope.Menu));
        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(bookings.Confirmation, PinScope.Menu));
    }

    [Fact]
    public async Task Menu_confirmations_never_open_the_bookings_window()
    {
        var (svc, _) = await SetUpAsync();

        await svc.ConfirmAsync(Pin, PinScope.Menu);

        Assert.False(svc.HasRecentConfirmation(PinScope.Bookings));
        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(null, PinScope.Bookings));
    }

    [Fact]
    public async Task A_bookings_confirmation_opens_a_three_minute_window_for_any_booking_action()
    {
        var (svc, _) = await SetUpAsync();
        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(null, PinScope.Bookings));

        await svc.ConfirmAsync(Pin, PinScope.Bookings);

        svc.EnsureConfirmed(null, PinScope.Bookings);
        _fx.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(59));
        svc.EnsureConfirmed(null, PinScope.Bookings); // still inside the window
        Assert.True(svc.HasRecentConfirmation(PinScope.Bookings));

        _fx.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(svc.HasRecentConfirmation(PinScope.Bookings));
        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(null, PinScope.Bookings));
    }

    [Fact]
    public async Task The_bookings_window_is_not_extended_by_later_confirmations()
    {
        var (svc, _) = await SetUpAsync();
        await svc.ConfirmAsync(Pin, PinScope.Bookings);
        _fx.Clock.Advance(TimeSpan.FromMinutes(2));

        await svc.ConfirmAsync(Pin, PinScope.Bookings);
        _fx.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        Assert.False(svc.HasRecentConfirmation(PinScope.Bookings)); // 3 min after the FIRST entry
    }

    [Fact]
    public async Task Locking_clears_the_bookings_window_and_unused_menu_confirmations()
    {
        var (svc, _) = await SetUpAsync();
        await svc.ConfirmAsync(Pin, PinScope.Bookings);
        var menu = await svc.ConfirmAsync(Pin, PinScope.Menu);

        svc.Lock();
        Assert.True((await svc.UnlockAsync(Pin)).Succeeded);

        Assert.False(svc.HasRecentConfirmation(PinScope.Bookings));
        Assert.Throws<PinRequiredException>(() => svc.EnsureConfirmed(menu.Confirmation, PinScope.Menu));
    }

    [Fact]
    public async Task Nothing_is_confirmable_while_locked()
    {
        var (svc, _) = await SetUpAsync();
        await svc.ConfirmAsync(Pin, PinScope.Bookings);
        svc.Lock();

        Assert.False(svc.HasRecentConfirmation(PinScope.Bookings));
        Assert.Throws<AccessDeniedException>(() => svc.EnsureConfirmed(null, PinScope.Bookings));
        await Assert.ThrowsAsync<AccessDeniedException>(() => svc.ConfirmAsync(Pin, PinScope.Menu));
    }

    [Fact]
    public async Task Lock_raises_the_change_event_only_when_the_state_changes()
    {
        var (svc, _) = await SetUpAsync();
        var changes = 0;
        svc.LockStateChanged += () => changes++;

        svc.Lock();
        svc.Lock();

        Assert.Equal(1, changes);
    }
}
