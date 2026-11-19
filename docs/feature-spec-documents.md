# Feature spec: document intake (Lab 6)

Employees submit medical certificates (MC) and expense claims. Claude reads the document and returns data in a fixed shape. C# checks the data. Anything that fails goes to a person.

## Flow
1. Employee submits a PDF on the Documents page (an MC or a claim, often a scan or a phone photo).
2. `POST /documents` (multipart form, field `file`) checks the file in C# **before any Claude call**: content type `application/pdf`, starts with `%PDF-`, max 5 MB, max 5 pages. Anything else → 400.
3. `DocumentExtractor.ExtractAsync(pdf)` sends the PDF to Claude as a **document block** (base64, `application/pdf`): Claude reads the text and also sees each page as an image.
4. Claude returns JSON that follows `DocumentExtractor.Schema` (structured outputs via `OutputConfig`).
5. `DocumentValidator.Check` runs the business rules in C#.
6. If checks fail: retry **once**, sending Claude its own answer and the exact problems.
7. Still failing, or kind is `other`: save as `NeedsReview`. Otherwise save as `Valid`.
8. HR sees `NeedsReview` items on the Review page (`GET /documents?status=NeedsReview`, HR role only; others get 403), with the employee's name, and opens the original PDF to compare (`GET /documents/{id}/file`).
   The document belongs to the signed-in employee (`User.EmployeeId()`), not an id from the body.

## Data
- Medical certificate: `employee_name`, `clinic`, `date_from`, `date_to`, `days`.
- Expense claim: `claim_no`, `vendor`, `date`, `currency`, `subtotal`, `tax`, `total`, `items[]` (`description`, `amount`).
- Dates `yyyy-MM-dd`. Amounts are numbers, no currency symbol.

## Rules (in C#, never only in the prompt)
- Required text fields are not empty.
- Dates parse as `yyyy-MM-dd`; `date_to` is not before `date_from`.
- MC `days` equals the number of calendar days from `date_from` to `date_to`, inclusive.
- Claim `total` = `subtotal` + `tax`; items add up to `subtotal`; currency is a 3-letter code.
- Claude must copy values as written. A document that is wrong on paper must end in `NeedsReview`, not be "fixed" by Claude.

## Privacy
- An MC is medical data (POL-12). The PDF is stored only so HR can compare it. `GET /documents/{id}/file`: the employee who sent it and HR; anyone else → 403.

## Limits
- Max 2 calls to Claude per document. Max 5 MB and 5 pages per PDF. `max_tokens` 1024.
- PDFs cost more tokens than plain text: every page is also read as an image.

## Scoring
`dotnet run --project tools/ExtractScore` sends the 20 sample PDFs in `seed/documents/` (some text PDFs, some scans) to `POST /documents/extract` and compares with `expected.json`. Score = 70% field accuracy + 30% correct status.
Samples 08, 10 and 17 are wrong on paper and should end in `NeedsReview`. Samples 19 and 20 are not MCs or claims.
The scorer prints each wrong field, for example `date: expected 2026-10-03, got 2026-03-10`.
