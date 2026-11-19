using Api.Documents;
using Api.Services;
using Xunit;

namespace Api.Tests;

public class DocumentExtractorTests
{
    static readonly ClaudeOptions Opts = new("claude-sonnet-5-5");
    static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n%%EOF"u8.ToArray();
    const string GoodMc = """{"kind":"medical_certificate","medical_certificate":{"employee_name":"Aisyah Rahman","clinic":"Klinik Mesra","date_from":"2026-10-05","date_to":"2026-10-06","days":2},"expense_claim":null}""";
    const string BadMc = """{"kind":"medical_certificate","medical_certificate":{"employee_name":"Aisyah Rahman","clinic":"Klinik Mesra","date_from":"2026-10-05","date_to":"2026-10-06","days":3},"expense_claim":null}""";
    const string GoodClaim = """{"kind":"expense_claim","medical_certificate":null,"expense_claim":{"claim_no":"EC-1","vendor":"Kedai","date":"2026-10-11","currency":"MYR","subtotal":100.00,"tax":6.00,"total":106.00,"items":[{"description":"Book","amount":100.00}]}}""";

    [Fact]
    public async Task Valid_first_time_is_Valid_with_one_call_and_schema_sent()
    {
        var fake = new FakeClaude(FakeClaude.Text(GoodMc));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.Valid, r.Status);
        Assert.Equal(1, r.Attempts);
        Assert.Single(fake.Requests);
        Assert.NotNull(fake.Requests[0].OutputConfig);
    }

    [Fact]
    public async Task Invalid_then_fixed_retries_once_with_the_error()
    {
        var fake = new FakeClaude(FakeClaude.Text(BadMc), FakeClaude.Text(GoodMc));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.Valid, r.Status);
        Assert.Equal(2, r.Attempts);
        Assert.Equal(3, fake.Requests[1].Messages.Count); // document, Claude's answer, the error
    }

    [Fact]
    public async Task Invalid_twice_goes_to_NeedsReview_and_stops()
    {
        var fake = new FakeClaude(FakeClaude.Text(BadMc), FakeClaude.Text(BadMc), FakeClaude.Text(GoodMc));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.NeedsReview, r.Status);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Contains(r.Problems, p => p.Contains("days"));
    }

    [Fact]
    public async Task Not_json_goes_to_NeedsReview()
    {
        var fake = new FakeClaude(FakeClaude.Text("sorry"), FakeClaude.Text("still not json"));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.NeedsReview, r.Status);
    }

    [Fact]
    public async Task Other_document_is_NeedsReview_without_retry()
    {
        var fake = new FakeClaude(FakeClaude.Text("""{"kind":"other","medical_certificate":null,"expense_claim":null}"""));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.NeedsReview, r.Status);
        Assert.Single(fake.Requests);
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    public async Task Stop_reason_other_than_end_turn_is_NeedsReview_without_retry(string stop)
    {
        var fake = new FakeClaude(FakeClaude.Text(GoodMc, stop), FakeClaude.Text(GoodMc));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.NeedsReview, r.Status);
        Assert.Single(fake.Requests);
        Assert.Equal(1, r.Attempts);
        Assert.Contains(r.Problems, p => p.Contains(stop));
    }

    [Fact]
    public async Task Retry_message_has_the_exact_problem_and_says_keep_values_as_written()
    {
        var fake = new FakeClaude(FakeClaude.Text(BadMc), FakeClaude.Text(GoodMc));
        await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        var retry = FakeClaude.MessageJson(fake.Requests[1], 2);
        Assert.Equal("user", retry.GetProperty("role").GetString());
        var text = retry.GetProperty("content").ToString();
        Assert.Contains("days is 3 but the date range is 2 days", text);
        Assert.Contains("correct only reading mistakes", text);
        Assert.Contains("keep its values as written", text);
        Assert.Equal("assistant", FakeClaude.MessageJson(fake.Requests[1], 1).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Document_wrong_on_paper_keeps_its_values_as_written()
    {
        var fake = new FakeClaude(FakeClaude.Text(BadMc), FakeClaude.Text(BadMc));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.NeedsReview, r.Status);
        Assert.Equal(2, r.Attempts);
        Assert.Equal(3, r.Data!.MedicalCertificate!.Days); // as written on the MC, not "fixed" to 2
    }

    [Fact]
    public async Task Not_json_retries_once_then_stops()
    {
        var fake = new FakeClaude(FakeClaude.Text("sorry"), FakeClaude.Text("still not json"), FakeClaude.Text(GoodMc));
        var r = await new DocumentExtractor(fake, Opts).ExtractAsync(Pdf);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Null(r.Data);
        Assert.Contains(r.Problems, p => p.Contains("JSON"));
    }

    [Fact]
    public async Task Valid_claim_passes()
    {
        var r = await new DocumentExtractor(new FakeClaude(FakeClaude.Text(GoodClaim)), Opts).ExtractAsync(Pdf);
        Assert.Equal(DocumentStatus.Valid, r.Status);
        Assert.Equal(106.00m, r.Data!.ExpenseClaim!.Total);
    }
}

public class DocumentPdfTests
{
    static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n%%EOF"u8.ToArray();

    [Fact]
    public async Task The_pdf_goes_to_Claude_as_a_document_block()
    {
        var fake = new FakeClaude(FakeClaude.Text("""{"kind":"other","medical_certificate":null,"expense_claim":null}"""));
        await new DocumentExtractor(fake, new ClaudeOptions("claude-sonnet-5-5")).ExtractAsync(Pdf);
        var first = FakeClaude.MessageJson(fake.Requests[0], 0).GetProperty("content")[0];
        Assert.Equal("document", first.GetProperty("type").GetString());
        Assert.Equal("application/pdf", first.GetProperty("source").GetProperty("media_type").GetString());
    }

    [Theory]
    [InlineData("text/plain", "%PDF-1.4", "PDF files")]
    [InlineData("application/pdf", "hello", "not a PDF")]
    public void Not_a_pdf_is_refused(string type, string content, string why) =>
        Assert.Contains(why, PdfUpload.Problem(type, System.Text.Encoding.ASCII.GetBytes(content)));

    [Fact]
    public void Too_many_pages_is_refused()
    {
        var six = "%PDF-1.4\n" + string.Concat(Enumerable.Repeat("<< /Type /Page >>\n", 6)) + "<< /Type /Pages >>";
        Assert.Contains("pages", PdfUpload.Problem("application/pdf", System.Text.Encoding.ASCII.GetBytes(six)));
        Assert.Null(PdfUpload.Problem("application/pdf", Pdf));
    }

    [Fact]
    public void Every_sample_document_is_a_pdf_the_api_accepts()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "documents");
        var files = Directory.GetFiles(dir, "*.pdf");
        Assert.Equal(20, files.Length);
        foreach (var f in files) Assert.Null(PdfUpload.Problem("application/pdf", File.ReadAllBytes(f)));
    }
}

public class DocumentValidatorTests
{
    static Extraction Claim(decimal sub, decimal tax, decimal total) =>
        new("expense_claim", null, new ExpenseClaim("EC-1", "V", "2026-10-01", "MYR", sub, tax, total, [new("x", sub)]));

    [Fact] public void Total_must_equal_subtotal_plus_tax() => Assert.Contains(DocumentValidator.Check(Claim(24, 1.44m, 26.44m)), p => p.Contains("total"));
    [Fact] public void Correct_claim_has_no_problems() => Assert.Empty(DocumentValidator.Check(Claim(24, 1.44m, 25.44m)));
    [Fact] public void Date_to_before_date_from_is_a_problem() =>
        Assert.Contains(DocumentValidator.Check(new("medical_certificate", new MedicalCertificate("A", "K", "2026-10-30", "2026-10-29", 1), null)), p => p.Contains("before"));
    [Fact] public void Bad_date_format_is_a_problem() =>
        Assert.Contains(DocumentValidator.Check(new("medical_certificate", new MedicalCertificate("A", "K", "30/10/2026", "2026-10-30", 1), null)), p => p.Contains("yyyy-MM-dd"));
}
