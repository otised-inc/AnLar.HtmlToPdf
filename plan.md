# Plan: Fast (non-508-compliant) PDF processing mode

## Goal
Add an **opt-in fast path** that generates a plain, *untagged* PDF — trading away
508 / PDF-UA accessibility for speed. The default behavior stays fully accessible
(backward compatible); callers explicitly request the fast path per request.

## Why this is faster
The current generator always produces a tagged PDF/UA document. The accessibility
work is the dominant per-request overhead:

1. **`pdfDocument.SetTagged()`** — iText builds and maintains a full structure
   (tag) tree as every element is laid out. This is the biggest single cost; an
   untagged document skips the entire structure-tree construction and the marked-
   content operators that wrap every piece of text/figure.
2. **Custom `AccessibleTagWorkerFactory`** — extra per-element tag workers for
   headings (H1–H6 role wiring) and images (Figure role + alt-text resolution).
3. **PDF/UA XMP metadata** + `pdfuaid` schema registration, `SetLang`,
   `DisplayDocTitle` viewer preference.

Fast mode skips 1–3, so iText runs its plain layout pipeline.

## Design

Add a single nullable boolean to the request: **`Accessible`** (default `true`).

- `Accessible == true` (or omitted) → current behavior, unchanged.
- `Accessible == false` → fast path: untagged PDF, default tag-worker factory,
  no PDF/UA XMP.

Naming chosen as `Accessible` (rather than `FastMode`) because it names the
*property of the output* rather than an implementation detail, and reads well as
a default-true opt-out. The README and code already frame everything around
"accessible PDFs", so `accessible: false` is the natural inverse.

Stamping features (page numbers, watermark, footer) keep working in both modes.
Their artifact marked-content is harmless on an untagged doc, so that code is
unchanged.

## Changes

### 1. `DTOs/PdfRequest.cs`
- Add `public bool? Accessible { get; set; }` with an XML-doc comment explaining
  that `false` produces a faster, non-508-compliant (untagged) PDF; default `true`.
- (Drive-by) fix the malformed XML-doc comment above `Dpi` (missing `<summary>`
  opening tag) while editing this file.

### 2. `Services/AccessiblePdfGenerator.cs`
- Add parameter `bool accessible = true` to `GenerateAccessiblePdfFromHtml`
  (keep it last so existing positional callers/tests are unaffected).
- Guard the accessibility setup behind `if (accessible)`:
  - `SetTagged()`, `SetLang(...)`, `SetViewerPreferences(DisplayDocTitle)`,
    and `SetPdfUaXmpMetadata(...)`.
  - `documentInfo.SetTitle(documentTitle)` stays in **both** modes (cheap, useful
    metadata, not accessibility-specific).
- Only attach the custom tag-worker factory when `accessible`:
  `if (accessible) converterProperties.SetTagWorkerFactory(_tagWorkerFactory);`
  In fast mode iText uses its default workers (no Figure/heading role wiring).
  `OutlineHandler` (bookmarks) is **kept in both modes** — bookmarks are a
  navigation convenience, not a 508 requirement, and cost little.
- Thread `accessible` through to `GeneratePdfPageImages` and
  `GeneratePdfPageImagesStreamAsync` as a parameter.
  - **Default the image endpoints to `accessible = false`**: their output is a
    rasterized PNG, so the structure tree is pure waste there. This is a free
    speed win with no observable output difference. (Call out for review — easy
    to make it honor the request flag instead if preferred.)
  - Also pass `request.FooterContent` through from these image paths, which is
    currently dropped (minor pre-existing gap; fixing it keeps parity with `/pdf`).

### 3. `Controllers/PdfController.cs`
- `/pdf`: pass `request.Accessible ?? true` as the new argument.
- `/pdf/images` and `/pdf/images/stream`: pass `request.Accessible ?? false`
  (raster output — fast by default), and also forward `request.FooterContent`.

### 4. Tests — `AnLar.HtmlToPdf.Tests/FastModeTests.cs` (new)
Following the existing `FooterTests` pattern (NullLogger, read back with `PdfReader`):
1. `GeneratePdf_AccessibleFalse_ProducesUntaggedPdf` — `accessible:false` →
   `pdfDoc.IsTagged()` is **false**, still a valid multi-byte PDF, ≥1 page.
2. `GeneratePdf_AccessibleTrue_ProducesTaggedPdf` — `accessible:true` →
   `IsTagged()` true (regression guard for the default).
3. `GeneratePdf_AccessibleDefault_IsTagged` — omitting the arg defaults to tagged.
4. `GeneratePdf_FastMode_WithStampingFeatures_ProducesValidPdf` — fast mode +
   page numbers + watermark + footer still produces a valid, untagged PDF.
5. `GeneratePdf_FastMode_NoPdfUaMetadata` — fast-mode output has no `pdfuaid`
   part in XMP (assert metadata absent / not PDF-UA).

### 5. `README.md`
- Add the `accessible` field to the `POST /pdf` request table (default `true`,
  "set `false` for a faster, non-accessible/untagged PDF").
- Add a short note that `/pdf/images*` render untagged by default since output is
  raster.

## File summary
| File | Action |
|------|--------|
| `DTOs/PdfRequest.cs` | Add `Accessible` flag; fix `Dpi` doc comment |
| `Services/AccessiblePdfGenerator.cs` | `accessible` param; gate tagging/XMP/tag-worker; thread through image methods (+forward footer) |
| `Controllers/PdfController.cs` | Pass `Accessible`; default image endpoints to fast; forward footer |
| `AnLar.HtmlToPdf.Tests/FastModeTests.cs` | New test file (5 tests) |
| `README.md` | Document the flag |

## Key decisions
- **Default stays accessible** — no behavior change unless `accessible:false`.
- **Title kept in both modes** — it's metadata, not tagging cost.
- **Bookmarks kept in both modes** — navigation aid, not a 508 requirement.
- **Image endpoints fast by default** — raster output never benefits from tags.

## Verification
- `dotnet build -c Release` then `dotnet test`.
- (Optional) quick local timing: POST the same large HTML with `accessible:true`
  vs `false` and compare response latency.
