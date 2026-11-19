using System.Text.Json;
using Anthropic.Models.Messages;
using Api.Services;

namespace Api.Documents;

/// <summary>Lab 6: turn a document into checked data, or send it to a person.</summary>
public class DocumentExtractor(IClaudeChat claude, ClaudeOptions options)
{
    public const int MaxAttempts = 2; // first try + one retry

    const string Prompt = """
        Extract the data from this Acme HR document. It is a medical certificate (MC), an expense claim, or something else (kind "other").
        Dates as yyyy-MM-dd. Numeric dates such as 03/10/2026 are written day first (dd/MM/yyyy), as in Malaysia: 03/10/2026 is 3 October 2026.
        Amounts as numbers without currency symbols. Use null for the part that does not apply.
        Copy values exactly as written. Do not fix or invent numbers.
        """;

    /// <summary>The JSON schema Claude must follow (structured outputs).</summary>
    public static readonly Dictionary<string, JsonElement> Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "kind", "medical_certificate", "expense_claim" },
        properties = new
        {
            kind = new { type = "string", @enum = new[] { "medical_certificate", "expense_claim", "other" } },
            medical_certificate = new
            {
                type = new[] { "object", "null" },
                additionalProperties = false,
                required = new[] { "employee_name", "clinic", "date_from", "date_to", "days" },
                properties = new
                {
                    employee_name = new { type = "string" }, clinic = new { type = "string" },
                    date_from = new { type = "string" }, date_to = new { type = "string" }, days = new { type = "integer" },
                },
            },
            expense_claim = new
            {
                type = new[] { "object", "null" },
                additionalProperties = false,
                required = new[] { "claim_no", "vendor", "date", "currency", "subtotal", "tax", "total", "items" },
                properties = new
                {
                    claim_no = new { type = "string" }, vendor = new { type = "string" }, date = new { type = "string" },
                    currency = new { type = "string" }, subtotal = new { type = "number" }, tax = new { type = "number" }, total = new { type = "number" },
                    items = new
                    {
                        type = "array",
                        items = new { type = "object", additionalProperties = false, required = new[] { "description", "amount" }, properties = new { description = new { type = "string" }, amount = new { type = "number" } } },
                    },
                },
            },
        },
    }).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    /// <summary>The first message: the PDF as a document block (Claude reads its text and its pages as images), then the ask.</summary>
    public static UserMessageParam DocumentMessage(byte[] pdf) => new()
    {
        Content = new List<ContentBlockParam>
        {
            new DocumentBlockParam { Source = new Base64PdfSource { Data = Convert.ToBase64String(pdf) } },
            new TextBlockParam { Text = "Extract the data from this document." },
        },
    };

    /// <summary>
    /// Claude gives the shape (JSON schema), C# decides if it is right (DocumentValidator), a person reviews the rest.
    /// At most <see cref="MaxAttempts"/> calls: one retry with the exact problems, then NeedsReview.
    /// </summary>
    public async Task<ExtractionResult> ExtractAsync(byte[] pdf, CancellationToken ct = default)
    {
        var history = new List<MessageParam> { DocumentMessage(pdf) };
        Extraction? data = null;
        List<string> problems = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var res = await claude.SendAsync(new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = 1024,
                System = Prompt,
                Messages = history.ToList(), // a copy: each request keeps the history as it was when sent
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = Schema } },
            }, ct);

            // max_tokens means cut-off JSON; a refusal will not improve on retry. A person looks at it.
            if (res.StopReason != StopReason.EndTurn)
                return new ExtractionResult(data, DocumentStatus.NeedsReview, [$"Claude stopped early (stop reason: {res.StopReason?.Raw() ?? "unknown"})"], attempt);

            var text = string.Concat(res.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            if (TryParse(text, out var parsed))
            {
                data = parsed;
                problems = DocumentValidator.Check(parsed);
                if (problems.Count == 0) return new ExtractionResult(data, DocumentStatus.Valid, [], attempt);
                // A retry cannot turn a letter into an MC or a claim.
                if (parsed.Kind == "other") return new ExtractionResult(data, DocumentStatus.NeedsReview, problems, attempt);
            }
            else problems = ["the answer is not valid JSON"];

            if (attempt == MaxAttempts) return new ExtractionResult(data, DocumentStatus.NeedsReview, problems, attempt);

            history.Add(new AssistantMessageParam { Content = text });
            history.Add(new UserMessageParam { Content = RetryMessage(problems) });
        }

        return new ExtractionResult(data, DocumentStatus.NeedsReview, problems, MaxAttempts); // not reached
    }

    /// <summary>The retry: the exact problems, and the rule that a document wrong on paper stays as written
    /// (samples 08, 10, 17 must end in NeedsReview, not be "fixed" so the checks pass).</summary>
    static string RetryMessage(IEnumerable<string> problems) =>
        "Your answer has these problems:\n" + string.Concat(problems.Select(p => $"- {p}\n")) +
        "\nRead the document again and correct only reading mistakes. If the document itself is wrong, keep its values as written: " +
        "do not change numbers or dates to make the checks pass.";

    static bool TryParse(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Extraction? data)
    {
        try { data = JsonSerializer.Deserialize<Extraction>(text); }
        catch (JsonException) { data = null; }
        return data is not null;
    }
}
