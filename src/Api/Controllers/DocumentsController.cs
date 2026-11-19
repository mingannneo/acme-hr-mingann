using Api.Auth;
using Api.Data;
using Api.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Api.Controllers;

/// <summary>Documents arrive as PDF files (multipart form field "file"). The document belongs to the
/// signed-in employee; there is no employeeId in the request.</summary>
[ApiController, Route("documents")]
public class DocumentsController(DocumentExtractor extractor, AcmeDb db) : ControllerBase
{
    /// <summary>Extract only, nothing saved. Used by tools/ExtractScore.</summary>
    [HttpPost("extract"), RequestSizeLimit(PdfUpload.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Extract(IFormFile? file, CancellationToken ct)
    {
        var (pdf, problem) = await ReadAsync(file, ct);
        if (problem is not null) return problem;
        return Ok(await extractor.ExtractAsync(pdf!, ct));
    }

    /// <summary>Intake: check the file, extract, check the data, save. Invalid documents are saved as NeedsReview for a person.</summary>
    [HttpPost, RequestSizeLimit(PdfUpload.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        var (pdf, problem) = await ReadAsync(file, ct);
        if (problem is not null) return problem;
        var r = await extractor.ExtractAsync(pdf!, ct);
        var doc = new StoredDocument
        {
            EmployeeId = User.EmployeeId(),
            FileName = Path.GetFileName(file!.FileName),
            Kind = r.Data?.Kind ?? "unknown",
            ExtractedJson = r.Data is null ? "" : JsonSerializer.Serialize(r.Data),
            Status = r.Status,
            Problems = string.Join("; ", r.Problems),
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        db.DocumentFiles.Add(new StoredDocumentFile { DocumentId = doc.Id, Pdf = pdf! });
        await db.SaveChangesAsync(ct);
        return Created($"/documents/{doc.Id}", doc);
    }

    /// <summary>The review queue holds other people's MCs and claims: HR only.</summary>
    [HttpGet, Authorize(Roles = Roles.HR)]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, CancellationToken ct)
    {
        var rows = await (from d in db.Documents
                          join e in db.Employees on d.EmployeeId equals e.Id into emp
                          from e in emp.DefaultIfEmpty()
                          where status == null || d.Status == status
                          orderby d.Id descending
                          select new { d, Name = e == null ? "" : e.Name }).Take(100).ToListAsync(ct);
        return Ok(rows.Select(x => new DocumentRow(x.d.Id, x.d.EmployeeId, x.Name, x.d.FileName, x.d.Kind, x.d.ExtractedJson, x.d.Status, x.d.Problems,
            new DateTimeOffset(DateTime.SpecifyKind(x.d.CreatedAt, DateTimeKind.Utc)))));
    }

    /// <summary>The original PDF: the employee who sent it, or HR. Anyone else gets 403.</summary>
    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> File(int id, CancellationToken ct)
    {
        var doc = await db.Documents.FindAsync([id], ct);
        if (doc is null) return Problem(statusCode: 404, title: $"Document {id} not found");
        if (doc.EmployeeId != User.EmployeeId() && !User.IsInRole(Roles.HR))
            return Problem(statusCode: 403, title: "Only the employee who sent it and HR may open this document");
        var file = await db.DocumentFiles.FindAsync([id], ct);
        return file is null ? Problem(statusCode: 404, title: "The original file was not kept") : File(file.Pdf, "application/pdf", doc.FileName);
    }

    async Task<(byte[]? Pdf, IActionResult? Problem)> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null) return (null, Problem(statusCode: 400, title: "Send the document as a PDF file in the form field \"file\"."));
        if (file.Length > PdfUpload.MaxBytes) return (null, Problem(statusCode: 400, title: $"The file is larger than {PdfUpload.MaxBytes / 1024 / 1024} MB."));
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        return PdfUpload.Problem(file.ContentType, bytes) is { } why ? (null, Problem(statusCode: 400, title: why)) : (bytes, null);
    }
}
