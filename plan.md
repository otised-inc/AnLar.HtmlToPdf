# Plan: Add Page Footer Support

## Overview
Add a configurable footer that renders on every page of the generated PDF. The footer will support plain text content with optional alignment, font size, and separator line — rendered in the stamping pass alongside existing page numbers and watermarks, marked as a PDF artifact for accessibility compliance.

## Changes

### 1. Add footer properties to `PdfRequest` DTO
**File:** `AnLar.HtmlToPdf/DTOs/PdfRequest.cs`

Add the following new properties:
- **`FooterText`** (`string?`, default `null`) — Text to display in the footer. When null/empty, no footer is rendered. Supports the placeholder `{pageNumber}` and `{totalPages}` for dynamic page numbering within the footer text.
- **`FooterFontSize`** (`float?`, default `9`) — Font size in points for footer text.
- **`FooterAlignment`** (`string?`, default `"center"`) — Horizontal alignment: `"left"`, `"center"`, or `"right"`.
- **`FooterShowLine`** (`bool?`, default `false`) — When true, draws a thin horizontal line above the footer text as a separator.

### 2. Thread footer parameters through the controller
**File:** `AnLar.HtmlToPdf/Controllers/PdfController.cs`

- Pass the new footer properties from `PdfRequest` to `GenerateAccessiblePdfFromHtml()`.
- Add validation: `FooterAlignment` must be `"left"`, `"center"`, or `"right"` (if provided).

### 3. Add footer parameters to `GenerateAccessiblePdfFromHtml`
**File:** `AnLar.HtmlToPdf/Services/AccessiblePdfGenerator.cs`

Add new parameters to the method signature:
- `string? footerText = null`
- `float footerFontSize = 9f`
- `string footerAlignment = "center"`
- `bool footerShowLine = false`

Update the `needsStamping` check to also trigger when `footerText` is non-empty.

### 4. Implement `AddFooter` method
**File:** `AnLar.HtmlToPdf/Services/AccessiblePdfGenerator.cs`

Create a new private static method `AddFooter(PdfDocument, string footerText, float fontSize, string alignment, bool showLine)`:

- Iterate over all pages (same pattern as `AddPageNumbers` / `AddWatermark`).
- For each page:
  1. Replace `{pageNumber}` and `{totalPages}` placeholders in the footer text with actual values.
  2. Calculate x position based on alignment:
     - `"left"` → left margin offset (~36pt / 0.5 inch from left edge)
     - `"center"` → centered on page width
     - `"right"` → right margin offset (~36pt from right edge)
  3. If `showLine` is true, draw a thin horizontal line across the page width (within margins) above the footer text.
  4. Render the text at y=20 (slightly above where page numbers sit at y=10, or at y=10 if page numbers are disabled — we'll use a fixed position at the bottom margin area).
  5. Wrap everything in `BeginMarkedContent(PdfName.Artifact)` / `EndMarkedContent()` for PDF/UA accessibility compliance.

**Positioning detail:** The footer renders at y=18 from the bottom. If `ShowPageNumbers` is also enabled, page numbers render below the footer at y=10 (existing behavior unchanged). This gives clear visual separation.

### 5. Adjust bottom margin when footer is active
When a footer is requested, the `WrapInHtmlDocument` method should ensure the bottom margin is large enough to accommodate the footer without overlapping body content. If `marginBottom` is less than 20mm when a footer is active, it will be clamped to at least 20mm.

### 6. Add tests
**File:** `AnLar.HtmlToPdf.Tests/InlineImageTests.cs` (or a new `FooterTests.cs`)

Add tests:
1. **`GeneratePdf_WithFooterText_ProducesValidPdf`** — Verifies PDF is generated successfully with footer text.
2. **`GeneratePdf_WithFooterAndPageNumbers_ProducesValidPdf`** — Verifies footer and page numbers coexist.
3. **`GeneratePdf_WithFooterPlaceholders_ReplacesPageNumbers`** — Verifies `{pageNumber}` / `{totalPages}` substitution works.
4. **`GeneratePdf_WithFooterLine_ProducesValidPdf`** — Verifies the separator line renders without error.
5. **`GeneratePdf_WithFooterAlignments_ProducesValidPdf`** — Tests left/center/right alignment all produce valid PDFs.

## File Summary
| File | Action |
|------|--------|
| `DTOs/PdfRequest.cs` | Add 4 new properties |
| `Controllers/PdfController.cs` | Pass footer props, add alignment validation |
| `Services/AccessiblePdfGenerator.cs` | Add params, `AddFooter()` method, update stamping logic, margin clamping |
| `Tests/FooterTests.cs` | New test file with 5 tests |
