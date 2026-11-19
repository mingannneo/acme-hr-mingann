// Lab 6 scorer: sends the 20 sample PDF documents to the API and compares with seed/documents/expected.json.
// Cost: one run is up to 40 Claude calls (20 PDFs x up to 2 attempts); each PDF page is also read as an image.
// Usage: dotnet run --project tools/ExtractScore [-- --api http://localhost:5080]
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

string Arg(string name, string def) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
var api = Arg("--api", Environment.GetEnvironmentVariable("ACME_API_URL") ?? "http://localhost:5080");
var dir = Path.Combine(AppContext.BaseDirectory, "documents");
var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "expected.json"))).RootElement;
using var http = new HttpClient { BaseAddress = new Uri(api), Timeout = TimeSpan.FromMinutes(2) };

// The API needs a signed-in user. Defaults to a training account (see README).
var email = Environment.GetEnvironmentVariable("ACME_EMAIL") ?? "dina.ismail4@acme-hr.example";
var password = Environment.GetEnvironmentVariable("SEED_USER_PASSWORD") ?? "Acme@2026";
var login = await http.PostAsJsonAsync("/auth/login", new { email, password });
if (!login.IsSuccessStatusCode) { Console.WriteLine($"Sign-in failed for {email}: HTTP {(int)login.StatusCode}"); Environment.Exit(1); }
http.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

int fieldsRight = 0, fieldsTotal = 0, statusRight = 0, docs = 0, handled = 0;
Console.WriteLine($"{"File",-14} {"Status (exp)",-26} Fields  Problems");
foreach (var e in expected.EnumerateObject())
{
    docs++;
    var pdf = new ByteArrayContent(File.ReadAllBytes(Path.Combine(dir, e.Name)));
    pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
    JsonElement res;
    try
    {
        var r = await http.PostAsync("/documents/extract", new MultipartFormDataContent { { pdf, "file", e.Name } });
        res = await r.Content.ReadFromJsonAsync<JsonElement>();
        if (!r.IsSuccessStatusCode) { Console.WriteLine($"{e.Name,-14} HTTP {(int)r.StatusCode}"); continue; }
    }
    catch (Exception ex) { Console.WriteLine($"{e.Name,-14} ERROR {ex.Message}"); continue; }

    var status = res.GetProperty("status").ToString();
    var expStatus = e.Value.GetProperty("status").GetString();
    if (status is "Valid" or "NeedsReview" or "0" or "1") handled++;
    var statusName = status == "0" ? "Valid" : status == "1" ? "NeedsReview" : status;
    if (statusName == expStatus) statusRight++;

    int right = 0, total = 0; var wrong = new List<string>();
    var kind = e.Value.GetProperty("kind").GetString()!;
    JsonElement data = res.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty(kind, out var k) ? k : default;
    foreach (var f in e.Value.GetProperty("fields").EnumerateObject())
    {
        total++;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(f.Name, out var got) && Same(f.Value, got)) right++;
        else wrong.Add($"{f.Name}: expected {f.Value}, got {(data.ValueKind == JsonValueKind.Object && data.TryGetProperty(f.Name, out var g) ? g.ToString() : "nothing")}");
    }
    fieldsRight += right; fieldsTotal += total;
    var problems = res.TryGetProperty("problems", out var p) ? string.Join("; ", p.EnumerateArray().Select(x => x.GetString())) : "";
    Console.WriteLine($"{e.Name,-14} {statusName + " (" + expStatus + ")",-26} {right}/{total}    {problems}");
    foreach (var w in wrong) Console.WriteLine($"{"",-14}   wrong {w}");
}

var accuracy = fieldsTotal == 0 ? 0 : 100.0 * fieldsRight / fieldsTotal;
var score = Math.Round(0.7 * accuracy + 30.0 * statusRight / docs, 1);
Console.WriteLine();
Console.WriteLine($"Handled (Valid or NeedsReview): {handled}/{docs}");
Console.WriteLine($"Status as expected:             {statusRight}/{docs}");
Console.WriteLine($"Field accuracy:                 {accuracy:F1}%");
Console.WriteLine($"SCORE: {score.ToString(CultureInfo.InvariantCulture)}");

static bool Same(JsonElement exp, JsonElement got) =>
    exp.ValueKind == JsonValueKind.Number && got.ValueKind == JsonValueKind.Number
        ? exp.GetDecimal() == got.GetDecimal()
        : string.Equals(exp.ToString().Trim(), got.ToString().Trim(), StringComparison.OrdinalIgnoreCase);
