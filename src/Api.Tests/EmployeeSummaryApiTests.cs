using System.Net;
using System.Net.Http.Json;

namespace Api.Tests;

/// <summary>GET /employees/{id}/summary: same access rules as the balance (own, direct reports, HR).</summary>
public class EmployeeSummaryApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Summary(int EmployeeId, decimal DaysTakenThisYear);

    [Fact]
    public async Task Employee_sees_their_own_summary()
    {
        var farah = await api.SignedInAs(People.Farah);
        var res = await farah.GetAsync("/employees/6/summary");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var summary = (await res.Content.ReadFromJsonAsync<Summary>())!;
        Assert.Equal(6, summary.EmployeeId);
        Assert.Equal(3, summary.DaysTakenThisYear); // seed: 3 approved days
    }

    [Fact]
    public async Task Manager_sees_a_direct_reports_summary()
    {
        var aina = await api.SignedInAs(People.Aina);
        Assert.Equal(HttpStatusCode.OK, (await aina.GetAsync("/employees/6/summary")).StatusCode);
    }

    [Fact]
    public async Task Another_employee_gets_403()
    {
        var gopal = await api.SignedInAs(People.Gopal);
        Assert.Equal(HttpStatusCode.Forbidden, (await gopal.GetAsync("/employees/6/summary")).StatusCode);
    }

    [Fact]
    public async Task Unknown_employee_is_404()
    {
        var hr = await api.SignedInAs(People.Dina); // HR passes the access check, so the id itself is what fails
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync("/employees/99999/summary")).StatusCode);
    }

    [Fact]
    public async Task Summary_needs_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/employees/6/summary")).StatusCode);
    }
}
