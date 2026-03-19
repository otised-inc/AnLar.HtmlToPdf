# Plan: Add HTML Page Footer Support

## Overview
Add an HTML footer that renders on every page of the generated PDF. The footer content is passed as HTML via a `FooterContent` property, rendered using iText's `HtmlConverter.ConvertToElements()`, and stamped onto each page in the second pass. The footer is marked as a PDF artifact so screen readers skip it, maintaining PDF/UA accessibility compliance.

## Approach: HTML Footer via Stamping Pass

The footer uses **the same two-pass architecture** as existing page numbers and watermarks:

1. **Pass 1**: Convert main body HTML to PDF (existing behavior, unchanged).
2. **Pass 2 (stamping)**: Re-open the PDF, and for each page:
   - Convert the footer HTML into iText layout elements using `HtmlConverter.ConvertToElements()`.
   - Create an `iText.Layout.Canvas` targeting a `Rectangle` in the bottom margin area of the page.
   - Mark the canvas content as a PDF artifact (`PdfName.Artifact`) for accessibility.
   - Add the converted elements to the canvas, which renders the HTML footer.

This approach means the footer supports **any HTML**: styled text, tables, images, links, etc. The same `ConverterProperties` (font provider, etc.) used for the main document are reused for the footer so fonts resolve consistently.

## Changes

### 1. Add `FooterContent` property to `PdfRequest` DTO
**File:** `AnLar.HtmlToPdf/DTOs/PdfRequest.cs`

Add one new property:
- **`FooterContent`** (`string?`, default `null`) — HTML content to render as a footer on every page. When null or empty, no footer is rendered. Can contain any valid HTML/CSS (inline styles, tables, images, etc.).

### 2. Thread `FooterContent` through the controller
**File:** `AnLar.HtmlToPdf/Controllers/PdfController.cs`

- Pass `request.FooterContent` to `GenerateAccessiblePdfFromHtml()`.
- No special validation needed — the HTML is passed through to iText's converter.

### 3. Update `GenerateAccessiblePdfFromHtml` signature and flow
**File:** `AnLar.HtmlToPdf/Services/AccessiblePdfGenerator.cs`

Add parameter: `string? footerContent = null`

Update the method flow:
- Build `ConverterProperties` (font provider, tag worker factory) **before** the first pass and store it so it can be reused in the stamping pass for footer HTML conversion.
- Update `needsStamping` to also trigger when `footerContent` is non-empty.
- Pass `converterProperties` and `footerContent` to the new `AddFooter` method.

### 4. Implement `AddFooter` method
**File:** `AnLar.HtmlToPdf/Services/AccessiblePdfGenerator.cs`

New private static method:
```
AddFooter(PdfDocument stampDoc, string footerHtml, ConverterProperties converterProperties)
```

For each page in the document:
1. Get page dimensions via `page.GetPageSize()`.
2. Define a footer `Rectangle` in the bottom margin area (e.g., full page width with a height of ~50pt, positioned at the bottom of the page). Use a margin inset of ~36pt (0.5in) on left/right to keep the footer within printable area.
3. Create a `PdfCanvas` on the page.
4. Call `canvas.BeginMarkedContent(PdfName.Artifact)` to mark the entire footer as an artifact.
5. Create an `iText.Layout.Canvas` (the high-level layout canvas) from the `PdfCanvas` and the footer rectangle.
6. Convert the footer HTML to elements via `HtmlConverter.ConvertToElements(footerHtml, converterProperties)`.
7. Add each element to the layout canvas.
8. Close the layout canvas and call `pdfCanvas.EndMarkedContent()`.

This renders fully styled HTML in the footer area of every page, marked as an artifact for PDF/UA compliance.

### 5. Adjust bottom margin when footer is active
**File:** `AnLar.HtmlToPdf/Services/AccessiblePdfGenerator.cs`

In `GenerateAccessiblePdfFromHtml`, when `footerContent` is non-empty, clamp `marginBottom` to at least 20mm so the main body content doesn't overlap the footer area.

### 6. Add tests
**File:** `AnLar.HtmlToPdf.Tests/FooterTests.cs` (new file)

Tests:
1. **`GeneratePdf_WithFooterContent_ProducesValidPdf`** — Simple HTML footer (`<p>Footer text</p>`) produces a valid PDF.
2. **`GeneratePdf_WithStyledFooterContent_ProducesValidPdf`** — Footer with inline CSS styling renders without error.
3. **`GeneratePdf_WithFooterAndPageNumbers_ProducesValidPdf`** — Footer and `ShowPageNumbers=true` coexist on the same page.
4. **`GeneratePdf_WithFooterAndWatermark_ProducesValidPdf`** — Footer and watermark coexist.
5. **`GeneratePdf_WithNoFooterContent_MatchesOriginalBehavior`** — Null/empty footer produces the same output as before (no regression).

## File Summary
| File | Action |
|------|--------|
| `DTOs/PdfRequest.cs` | Add `FooterContent` property |
| `Controllers/PdfController.cs` | Pass `FooterContent` to generator |
| `Services/AccessiblePdfGenerator.cs` | Add param, refactor `ConverterProperties` for reuse, implement `AddFooter()` method, update stamping logic, margin clamping |
| `Tests/FooterTests.cs` | New test file with 5 tests |

## Key Design Decisions
- **HTML, not plain text**: Full HTML support means users can style footers with CSS, use tables for multi-column layouts, include images/logos, etc.
- **Single property**: Just `FooterContent` — alignment, font size, styling are all controlled via HTML/CSS within the content itself (e.g., `<div style="text-align:right; font-size:8pt">...</div>`).
- **Reuse ConverterProperties**: The footer HTML uses the same font provider and converter settings as the main document, so custom/bundled fonts work in the footer too.
- **Artifact marking**: The entire footer is wrapped in `BeginMarkedContent(PdfName.Artifact)` / `EndMarkedContent()` so screen readers and PDF/UA validators ignore it.
- **Same footer on every page**: The same HTML is rendered on each page. Dynamic per-page content (like page numbers) is not supported in this iteration since iText's `ConvertToElements` doesn't have a page-number concept.
