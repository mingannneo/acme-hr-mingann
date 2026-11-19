using System.Text;
using System.Text.RegularExpressions;

namespace Api.Documents;

/// <summary>Checks an uploaded file in C# before any Claude call: a real PDF, not too big, not too long.</summary>
public static partial class PdfUpload
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPages = 5;

    /// <summary>Null when the file is fine, otherwise the reason it is refused.</summary>
    public static string? Problem(string? contentType, byte[] bytes)
    {
        if (bytes.Length == 0) return "The file is empty.";
        if (bytes.Length > MaxBytes) return $"The file is larger than {MaxBytes / 1024 / 1024} MB.";
        if (contentType is not null && !contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)) return "Only PDF files are accepted (application/pdf).";
        if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") return "The file is not a PDF.";
        if (Pages(bytes) > MaxPages) return $"The PDF has more than {MaxPages} pages.";
        return null;
    }

    /// <summary>Counts page objects. Enough for a training check; a real system would use a PDF library.</summary>
    public static int Pages(byte[] bytes) => PageRegex().Matches(Encoding.Latin1.GetString(bytes)).Count;

    [GeneratedRegex(@"/Type\s*/Page(?![s\w])")]
    private static partial Regex PageRegex();
}
