using Api.Models;
using Api.Services;

namespace Api.Tests;

public class LeaveServiceTests
{
    [Fact]
    public async Task Balance_for_new_employee_is_full_entitlement()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(1);
        Assert.NotNull(b);
        Assert.Equal(b!.Entitlement, b.Available);
    }

    [Fact]
    public async Task Balance_for_unknown_employee_is_null()
    {
        using var db = TestDb.Create();
        Assert.Null(await new LeaveService(db).GetBalanceAsync(999));
    }

    [Fact]
    public async Task Approved_leave_reduces_balance()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(6); // seed: 3 approved days
        Assert.Equal(3, b!.Used);
    }

    [Fact]
    public async Task Half_day_counts_half()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(7); // seed: AM half day
        Assert.Equal(0.5m, b!.Used);
    }

    [Fact]
    public async Task Pending_leave_is_held_from_available()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(8); // seed: 5 pending days
        Assert.Equal(5, b!.Pending);
        Assert.Equal(b.Entitlement - 5, b.Available);
    }

    [Fact]
    public async Task List_filters_by_employee()
    {
        using var db = TestDb.Create();
        var list = await new LeaveService(db).ListAsync(6);
        Assert.All(list, r => Assert.Equal(6, r.EmployeeId));
    }

    // DaysTakenThisYearAsync: only Approved leave counts as taken.

    [Fact]
    public async Task Days_taken_counts_approved_leave()
    {
        using var db = TestDb.Create();
        Assert.Equal(3, await new LeaveService(db).DaysTakenThisYearAsync(6)); // seed: 3 approved days
    }

    [Fact]
    public async Task Days_taken_does_not_count_pending_leave()
    {
        using var db = TestDb.Create();
        Assert.Equal(0, await new LeaveService(db).DaysTakenThisYearAsync(8)); // seed: 5 pending days, none approved
    }

    [Fact]
    public async Task Days_taken_does_not_count_rejected_leave()
    {
        using var db = TestDb.Create();
        var day = Api.Data.Seed.MondayOnOrAfter(new DateOnly(DateTime.UtcNow.Year, 6, 1));
        db.LeaveRequests.Add(new LeaveRequest { EmployeeId = 6, StartDate = day, EndDate = day.AddDays(1), Status = LeaveStatus.Rejected });
        await db.SaveChangesAsync();

        Assert.Equal(3, await new LeaveService(db).DaysTakenThisYearAsync(6)); // still only the 3 approved seed days
    }

    [Fact]
    public void Rejected_leave_days_still_computed() =>
        Assert.Equal(1, LeaveService.DaysOf(new LeaveRequest { StartDate = new(2026, 11, 18), EndDate = new(2026, 11, 18), Status = LeaveStatus.Rejected }));
}
