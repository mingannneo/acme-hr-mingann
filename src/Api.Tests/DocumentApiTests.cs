using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests;

/// <summary>An API whose Claude is a FakeClaude, so document tests need no key.</summary>
public class FakeClaudeApiFactory : ApiFactory
{
    public FakeClaude Claude { get; } = new(Enumerable.Repeat(FakeClaude.Text("""{"kind":"other","medical_certificate":null,"expense_claim":null}"""), 20).ToArray());
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IClaudeChat>(Claude));
    }
}

/// <summary>PDF upload over HTTP: checked in C# before Claude, and only the sender and HR may open the file.</summary>
public class DocumentApiTests(FakeClaudeApiFactory api) : IClassFixture<FakeClaudeApiFactory>
{
    static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n%%EOF"u8.ToArray();

    static MultipartFormDataContent Form(byte[] bytes, string type, string name = "mc.pdf")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task A_text_file_is_400_and_Claude_is_never_called()
    {
        var farah = await api.SignedInAs(People.Farah);
        var before = api.Claude.Requests.Count;
        var res = await farah.PostAsync("/documents", Form("Sijil Cuti Sakit"u8.ToArray(), "text/plain", "mc.txt"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(before, api.Claude.Requests.Count);
    }

    [Fact]
    public async Task A_file_over_5_MB_is_400()
    {
        var farah = await api.SignedInAs(People.Farah);
        var big = new byte[6 * 1024 * 1024]; "%PDF-"u8.CopyTo(big);
        var res = await farah.PostAsync("/documents/extract", Form(big, "application/pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Sender_and_HR_can_open_the_pdf_another_employee_gets_403()
    {
        var farah = await api.SignedInAs(People.Farah);
        var created = await farah.PostAsync("/documents", Form(Pdf, "application/pdf"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await farah.GetAsync($"/documents/{id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await api.SignedInAs(People.Dina)).GetAsync($"/documents/{id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.SignedInAs(People.Gopal)).GetAsync($"/documents/{id}/file")).StatusCode);
    }

    [Fact]
    public async Task Review_rows_have_the_employee_name_and_a_utc_time()
    {
        var farah = await api.SignedInAs(People.Farah);
        await farah.PostAsync("/documents", Form(Pdf, "application/pdf"));
        var json = await (await (await api.SignedInAs(People.Dina)).GetAsync("/documents")).Content.ReadAsStringAsync();
        Assert.Contains("\"employeeName\":\"Farah Tan\"", json);
        Assert.Matches("\"createdAt\":\"[^\"]+(Z|\\+00:00)\"", json);
    }
}
