using RoastIT.Core.Enums;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Services;

public enum BookingTab { List, Dashboard }

/// <summary>
/// Which date the Bookings page is looking at, shared by the Dashboard and the Booking list (plan §7.4, §10.4;
/// bookings spec §2–§5). Dates outside the schedulable window (today ± 6 months) are rejected here, whatever path
/// asks. Not remembered between app starts. Register as a singleton (one window, one scope).
/// </summary>
public sealed class BookingScopeState
{
    /// <summary>Days shown in the strip.</summary>
    public const int StripDays = 10;

    private readonly IBusinessCalendar _calendar;

    public BookingScopeState(IBusinessCalendar calendar)
    {
        _calendar = calendar;
        var today = calendar.Today();
        SelectedDate = today; // the page opens on today (D32); tapping it again shows all dates
        StripStart = ClampStripStart(today.AddDays(-2));
        CalendarMonth = FirstOfMonth(today);
    }

    /// <summary>Null = all dates (today ± 6 months, D32). Starts on today.</summary>
    public DateOnly? SelectedDate { get; private set; }

    public BookingTab ActiveTab { get; private set; } = BookingTab.List;

    /// <summary>First day of the 10-day strip.</summary>
    public DateOnly StripStart { get; private set; }

    /// <summary>First day of the month the calendar sheet shows.</summary>
    public DateOnly CalendarMonth { get; private set; }

    // ---- Booking list controls ----
    // They live here, not in the page, so they survive moving to Menu and back. They reset when the app restarts.

    public string Search { get; private set; } = "";

    /// <summary>Null = All.</summary>
    public ItemStatus? StatusFilter { get; private set; }

    public BookingSortField SortField { get; private set; } = BookingSortField.Pickup;

    public SortDirection SortDirection { get; private set; } = SortDirection.Asc;

    /// <summary>The list's filters and sort, with the selected date as its date filter.</summary>
    public BookingListOptions ListOptions => new(SelectedDate, Search, StatusFilter, SortField, SortDirection);

    public event Action? Changed;

    /// <summary>Typing in the search box. Doesn't raise <see cref="Changed"/>: the page re-renders itself.</summary>
    public void SetSearch(string? text) => Search = text ?? "";

    public void SetStatusFilter(ItemStatus? status) => StatusFilter = status;

    /// <summary>Changing the field keeps the direction (bookings spec §6.4).</summary>
    public void SetSort(BookingSortField field) => SortField = field;

    public void SetSortDirection(SortDirection direction) => SortDirection = direction;

    public DateOnly Today => _calendar.Today();
    public DateOnly Min => ScheduleWindow.Min(Today);
    public DateOnly Max => ScheduleWindow.Max(Today);

    public bool IsSelectable(DateOnly date) => ScheduleWindow.Contains(Today, date);

    public void SetTab(BookingTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        Changed?.Invoke();
    }

    /// <summary>
    /// Selects a date, or clears the selection when <paramref name="date"/> is null. Returns false (and changes nothing)
    /// for a date outside the window. A date outside the visible strip moves the strip to <c>date − 2</c>.
    /// </summary>
    public bool SelectDate(DateOnly? date)
    {
        if (date is not { } d)
        {
            if (SelectedDate is null) return true;
            SelectedDate = null;
            Changed?.Invoke();
            return true;
        }
        if (!IsSelectable(d)) return false;

        SelectedDate = d;
        CalendarMonth = FirstOfMonth(d);
        if (d < StripStart || d > StripStart.AddDays(StripDays - 1)) StripStart = ClampStripStart(d.AddDays(-2));
        Changed?.Invoke();
        return true;
    }

    /// <summary>Tapping a day tile or calendar cell: selects it, or clears the date when it is already selected.</summary>
    public bool ToggleDate(DateOnly date) => SelectDate(SelectedDate == date ? null : date);

    /// <summary>The calendar's Today button: selects today, shows its month, and resets the strip to today − 2.</summary>
    public void SelectToday()
    {
        var today = Today;
        SelectedDate = today;
        CalendarMonth = FirstOfMonth(today);
        StripStart = ClampStripStart(today.AddDays(-2));
        Changed?.Invoke();
    }

    public bool CanShiftStrip(int days) => ClampStripStart(StripStart.AddDays(days)) != StripStart;

    /// <summary>The ‹ and › arrows: moves the strip by a week, stopping at the window edges.</summary>
    public void ShiftStrip(int days)
    {
        var next = ClampStripStart(StripStart.AddDays(days));
        if (next == StripStart) return;
        StripStart = next;
        Changed?.Invoke();
    }

    /// <summary>Opening the calendar shows the selected date's month, or today's.</summary>
    public void OpenCalendar()
    {
        CalendarMonth = FirstOfMonth(SelectedDate ?? Today);
        Changed?.Invoke();
    }

    public bool CanShiftMonth(int months) => ClampMonth(CalendarMonth.AddMonths(months)) != CalendarMonth;

    public void ShiftMonth(int months)
    {
        var next = ClampMonth(CalendarMonth.AddMonths(months));
        if (next == CalendarMonth) return;
        CalendarMonth = next;
        Changed?.Invoke();
    }

    private DateOnly ClampStripStart(DateOnly start)
    {
        var min = Min;
        var max = Max.AddDays(-(StripDays - 1));
        return start < min ? min : start > max ? max : start;
    }

    private DateOnly ClampMonth(DateOnly month)
    {
        var min = FirstOfMonth(Min);
        var max = FirstOfMonth(Max);
        return month < min ? min : month > max ? max : month;
    }

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
}
