# AnLar.HtmlToPdf

An ASP.NET Core Web API that converts HTML content into accessible PDF documents compliant with the **PDF/UA** (Universal Accessibility) standard, with an opt-in fast (untagged) mode and PDF-to-PNG rendering. Built with [iText](https://itextpdf.com/) for reliable, structured PDF generation suitable for screen readers and assistive technologies.

## Features

- **PDF/UA-1 Compliance** — Tagged PDF structure with XMP metadata, document language, title, and `DisplayDocTitle` viewer preference (PDF 1.7)
- **HTML-to-PDF Conversion** — Accepts raw HTML fragments or full documents and returns a PDF binary
- **Fast (Untagged) Mode** — Set `"accessible": false` to skip tagging and PDF/UA metadata for a faster, smaller PDF when accessibility is not required; all other options still apply
- **Semantic Heading Structure** — Custom tag worker produces clean H1–H6 structure elements without iText's default intermediate `P` wrappers
- **List Markers in Reading Order** — Custom list-item tag worker keeps bullet characters (disc, circle, square) in the correct reading order for screen readers, including nested lists
- **Automatic Bookmarks** — Headings generate a PDF outline/bookmark tree via iText's `OutlineHandler`
- **Bundled Fonts** — Ships with Liberation Serif (Regular, Bold, Italic, Bold Italic) for body text and Pinyon Script for cursive/signature styles, so PDFs render consistently even on minimal Linux containers (see [Bundled Fonts & Licensing](#bundled-fonts--licensing))
- **Deterministic Font Fallback** — Fonts load from the bundled `Fonts/` directory (falling back to embedded assembly resources); system font directories are **not** scanned. Unmatched families always fall back to Liberation Serif, never to a decorative face, via a custom `FallbackSafeFontProvider`
- **Smart HTML Wrapping** — Automatically wraps partial HTML snippets in a complete document with `@page` margins, language attribute, and default serif typography. Full documents (starting with `<!DOCTYPE` or `<html`) pass through untouched
- **Page Layout Control** — Configurable page orientation (portrait/landscape) and per-side margins in millimeters (applies to HTML fragments; full documents control their own `@page` rules)
- **Optional Page Numbers** — Adds centered "Page X of Y" footers, marked as PDF artifacts to preserve accessibility compliance. Stamps use the bundled, embedded Liberation Serif so PDF/UA font-embedding checks still pass
- **Watermark Support** — Optional diagonal watermark text (e.g. "DRAFT", "CONFIDENTIAL") rendered in light gray with 30% opacity, marked as a PDF artifact so it doesn't interfere with screen readers
- **Custom HTML Footers** — Render arbitrary HTML/CSS as a footer on every page with `{pageNumber}` / `{totalPages}` placeholders, marked as a PDF artifact to preserve accessibility compliance. The bottom margin is raised to at least 20 mm to make room
- **Inline Image Support** — Handles base64-encoded and URL-referenced images with full 508/PDF-UA compliance: images with `alt` text are tagged as Figure elements, empty `alt=""` marks images as decorative (excluded from structure tree), and missing `alt` attributes receive a fallback description
- **PDF-to-Image Export** — Convert generated PDFs to high-quality PNG images at configurable DPI via the `/pdf/images` endpoint, or stream them page-by-page as NDJSON via `/pdf/images/stream` for large jobs
- **Compressed Requests** — All endpoints accept `Content-Encoding: gzip` / `br` / `deflate` request bodies
- **Background Warmup** — iText is pre-warmed on a background thread at startup so the first request avoids most of the cold-start cost

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

## Getting Started

```bash
# Clone the repository
git clone <repo-url>
cd AnLar.HtmlToPdf

# Restore dependencies
dotnet restore

# Run the development server
dotnet run --project AnLar.HtmlToPdf
```

The API will be available at `https://localhost:50670` (HTTPS) and `http://localhost:50671` (HTTP).

## Usage

### `POST /pdf`

Converts HTML content to an accessible PDF.

**Request Body (JSON):**

```json
{
  "htmlContent": "<h1>Hello World</h1><p>This is a sample document.</p>",
  "documentTitle": "My Document",
  "documentLanguage": "en-US",
  "pageOrientation": "portrait",
  "marginTop": 10,
  "marginRight": 10,
  "marginBottom": 10,
  "marginLeft": 10,
  "showPageNumbers": false,
  "watermark": "DRAFT",
  "footerContent": "<p style='text-align:center; font-size:8pt;'>Company Confidential</p>",
  "accessible": true
}
```

| Field              | Type    | Required | Default        | Description                                               |
|--------------------|---------|----------|----------------|-----------------------------------------------------------|
| `htmlContent`      | string  | Yes      | —              | HTML content to convert (fragment or full document)       |
| `documentTitle`    | string  | No       | `"Untitled"`   | Title embedded in PDF metadata                            |
| `documentLanguage` | string  | No       | `"en-US"`      | BCP 47 language tag (e.g. `en-US`, `nl-NL`)              |
| `pageOrientation`  | string  | No       | `"portrait"`   | Page orientation: `"portrait"` or `"landscape"`           |
| `marginTop`        | float   | No       | `10`           | Top margin in millimeters                                 |
| `marginRight`      | float   | No       | `10`           | Right margin in millimeters                               |
| `marginBottom`     | float   | No       | `10`           | Bottom margin in millimeters                              |
| `marginLeft`       | float   | No       | `10`           | Left margin in millimeters                                |
| `showPageNumbers`  | boolean | No       | `false`        | When `true`, adds "Page X of Y" centered at the bottom of each page |
| `watermark`        | string  | No       | `null`         | Diagonal watermark text rendered on every page (e.g. `"DRAFT"`, `"CONFIDENTIAL"`) |
| `footerContent`    | string  | No       | `null`         | HTML content rendered as a footer on every page (marked as artifact for accessibility) |
| `accessible`       | boolean | No       | `true`         | When `true`, produces a tagged 508/PDF-UA-compliant PDF. Set `false` for a faster, **non-accessible** (untagged) PDF when accessibility is not required |

**Response:** `application/pdf` binary stream. Validation failures return `400` with a plain-text message (e.g. `htmlContent is required.`).

> **Fragments vs. full documents:** `pageOrientation`, the four margins, `documentTitle` and `documentLanguage` are applied by wrapping an HTML *fragment* in a complete document. If `htmlContent` already starts with `<!DOCTYPE` or `<html`, it is passed through unchanged and must supply its own `@page` rules and `lang` attribute.

> **Note:** The `/pdf/images` and `/pdf/images/stream` endpoints render untagged PDFs by default (`accessible` defaults to `false` there), since their output is a rasterized PNG where the structure tree provides no benefit. Pass `"accessible": true` if you need the intermediate PDF tagged.

> **Compressed request bodies:** All endpoints accept `Content-Encoding: gzip` (also `br` / `deflate`) request bodies via ASP.NET Core request decompression — recommended for large `htmlContent` payloads, which typically shrink 5–8×. Plain (uncompressed) requests work unchanged.

**Example (curl):**

```bash
curl -X POST https://localhost:50670/pdf \
  -H "Content-Type: application/json" \
  -d '{"htmlContent":"<h1>Report</h1><p>Content here.</p>","documentTitle":"Report","documentLanguage":"en-US","showPageNumbers":true,"watermark":"DRAFT"}' \
  -o output.pdf
```

### `POST /pdf/images`

Renders each page of the generated PDF as a PNG image. Useful for previews and thumbnails.

**Request Body (JSON):** Same as `POST /pdf`, with one additional field:

| Field | Type | Required | Default | Description                          |
|-------|------|----------|---------|--------------------------------------|
| `dpi` | int  | No       | `300`   | Resolution (dots per inch) for the rendered images |

**Response (JSON):**

```json
{
  "pageCount": 2,
  "pages": [
    "<base64-encoded PNG of page 1>",
    "<base64-encoded PNG of page 2>"
  ]
}
```

**Example (curl):**

```bash
curl -X POST https://localhost:50670/pdf/images \
  -H "Content-Type: application/json" \
  -d '{"htmlContent":"<h1>Report</h1><p>Content here.</p>","documentTitle":"Report","dpi":150}' \
  -o response.json
```

> **Use `/pdf/images/stream` (below) for large jobs.** This endpoint buffers every page's base64 PNG in memory and serializes them all in one JSON response — for jobs over a few hundred pages on a memory-constrained host (e.g. Azure App Service), prefer the streaming variant.

### `POST /pdf/images/stream`

Streaming variant of `/pdf/images` for large or memory-sensitive jobs. Emits NDJSON (one JSON object per line) and flushes after every page, so server memory stays bounded regardless of total page count.

**Request Body (JSON):** Same as `POST /pdf/images`.

**Response:** `application/x-ndjson` — one `PageImage` JSON object per line, in the order pages finish encoding (which may differ from page order under parallel encoding; use the `page` field to reassemble). Validation failures return `400` with a JSON body `{"error": "..."}`.

```
{"page":0,"totalPages":3,"base64":"<png-1>"}
{"page":1,"totalPages":3,"base64":"<png-2>"}
{"page":2,"totalPages":3,"base64":"<png-3>"}
```

| Field        | Type   | Description                                  |
|--------------|--------|----------------------------------------------|
| `page`       | int    | Zero-based page index                        |
| `totalPages` | int    | Total page count (same on every line)        |
| `base64`     | string | Base64-encoded PNG for the page              |

**Example (curl, prints lines as they arrive):**

```bash
curl -N -X POST https://localhost:50670/pdf/images/stream \
  -H "Content-Type: application/json" \
  -d '{"htmlContent":"<h1>Report</h1>","documentTitle":"Report","dpi":150}'
```

## Project Structure

```
PDF Generator/
├── AnLar.HtmlToPdf.sln
├── .github/workflows/
│   ├── deploy-dev.yml                    # Deploys master → htmltopdfdevlinux (Azure Web App)
│   └── deploy-prod.yml                   # Deploys prod → htmltopdflinux (Azure Web App)
├── docs/                                 # User guides + technical docs (GitBook layout)
└── AnLar.HtmlToPdf/
    ├── AnLar.HtmlToPdf.csproj
    ├── Program.cs                        # App entry point, DI, request decompression, warmup
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── Controllers/
    │   └── PdfController.cs              # POST /pdf, /pdf/images, /pdf/images/stream
    ├── Services/
    │   ├── AccessiblePdfGenerator.cs     # Core PDF generation, tagging, stamping, rasterization
    │   └── FallbackSafeFontProvider.cs   # Deterministic font fallback (never Pinyon unless named)
    ├── DTOs/
    │   ├── PdfRequest.cs                 # Request model
    │   ├── PdfImagesResponse.cs          # Response model for /pdf/images
    │   └── PageImage.cs                  # NDJSON line model for /pdf/images/stream
    ├── Properties/
    │   └── launchSettings.json
    └── Fonts/
        ├── LiberationSerif-Regular.ttf
        ├── LiberationSerif-Bold.ttf
        ├── LiberationSerif-Italic.ttf
        ├── LiberationSerif-BoldItalic.ttf
        ├── PinyonScript-Regular.ttf
        ├── LICENSE-LiberationFonts.txt
        └── LICENSE-PinyonScript.txt
AnLar.HtmlToPdf.Tests/
    ├── CompliancePreservationTests.cs # Stamping keeps PDF/UA metadata & structure tree intact
    ├── FastModeTests.cs               # accessible:false produces untagged PDFs without PDF/UA metadata
    ├── FontFallbackTests.cs           # Fallback never resolves to Pinyon Script unless requested
    ├── FooterTests.cs                 # HTML footer rendering & placeholders
    ├── InlineImageTests.cs            # Image sources, alt text, Figure/decorative tagging
    ├── PdfToImageTests.cs             # PDF-to-image export
    └── RequestDecompressionTests.cs   # gzip request bodies (WebApplicationFactory integration tests)
```

Run the tests with:

```bash
dotnet test AnLar.HtmlToPdf.sln
```

## Build & Publish

```bash
# Build for release
dotnet build -c Release

# Publish for deployment
dotnet publish -c Release

# Publish for Linux (e.g. Azure Web App)
dotnet publish -c Release -r linux-x64
```

## Deployment

GitHub Actions deploys to Azure Web Apps (Linux) on push. Each workflow restores, builds, runs the test suite, publishes, and deploys with a publish-profile secret; failures are posted to Slack.

| Branch   | Workflow                              | Azure Web App          | Publish-profile secret              |
|----------|---------------------------------------|------------------------|-------------------------------------|
| `master` | `.github/workflows/deploy-dev.yml`    | `htmltopdfdevlinux`    | `HTMLTOPDFDEVLINUX_WEB_DEPLOYMENT`  |
| `prod`   | `.github/workflows/deploy-prod.yml`   | `htmltopdflinux`       | `HTMLTOPDFPRODLINUX_WEB_DEPLOYMENT` |

The prod workflow runs under the GitHub `production` environment and can also be triggered manually from the Actions tab.

## Dependencies

| Package                          | Version | Purpose                                   |
|----------------------------------|---------|-------------------------------------------|
| `itext.pdfhtml`                  | 6.3.2   | HTML-to-PDF conversion with iText         |
| `itext.bouncy-castle-adapter`    | 9.6.0   | Cryptography adapter required by iText    |
| `PDFtoImage`                     | 5.2.1   | PDF page rendering for `/pdf/images` endpoint |
| `Newtonsoft.Json`                | 13.0.4  | Pinned to override a vulnerable transitive pulled in by iText |

## Bundled Fonts & Licensing

Fonts in `AnLar.HtmlToPdf/Fonts/` are embedded in the assembly and copied to the deployment output. They are parsed once and cached for the process lifetime, and registered with a fresh iText font provider on every request. Request HTML can reference them by family name — no `@font-face` needed.

**Only bundled fonts are available.** System font directories are intentionally not scanned, so naming an installed system font (e.g. `Arial`) without an `@font-face` falls back to Liberation Serif. `@font-face` declarations in request HTML are honored when their source resolves.

| Font | CSS family name | Style | License |
|------|-----------------|-------|---------|
| [Liberation Serif](https://github.com/liberationfonts/liberation-fonts) (Regular, Bold, Italic, Bold Italic) | `Liberation Serif` | Serif body text (metric-compatible with Times New Roman) | SIL OFL 1.1 — `Fonts/LICENSE-LiberationFonts.txt` |
| [Pinyon Script](https://fonts.google.com/specimen/Pinyon+Script) | `Pinyon Script` | Cursive / signature styles | SIL OFL 1.1 — `Fonts/LICENSE-PinyonScript.txt` |

Example usage in request HTML:

```html
<span style="font-family: 'Pinyon Script', cursive; font-size: 28px;">Jane Doe</span>
```

### Why the bundled fonts are legal to embed

PDF/UA (accessible) output requires every font to be **embedded** in the generated PDF, and this service embeds fonts into documents generated **automatically, server-side, on behalf of arbitrary callers**. Both facts matter legally: most commercial font licenses do not cover this use.

Both bundled font families are licensed under the [SIL Open Font License 1.1](https://openfontlicense.org/) (OFL), which explicitly permits this service's exact usage:

- **Document embedding is allowed**, in full or as a subset. Per the [OFL FAQ](https://openfontlicense.org/ofl-faq/), a font embedded in a document is not considered redistribution of the font software, and the generated document does **not** inherit the OFL — output PDFs remain unencumbered.
- **Server-side / automated document generation is allowed.** The OFL places no restrictions on where the font is installed or whether documents are produced by an automated service.
- **Commercial use is allowed**, with no per-document or per-server fees.

Copyright holders: Liberation fonts © 2012 Red Hat, Inc. (Reserved Font Name "Liberation"); Pinyon Script © 2024 The Pinyon Project Authors, [github.com/SorkinType/Pinyon](https://github.com/SorkinType/Pinyon) (Reserved Font Name "Pinyon Script"). The bundled Pinyon Script files were obtained from the canonical [google/fonts](https://github.com/google/fonts/tree/main/ofl/pinyonscript) distribution.

### Obligations we must keep

- **Ship the license text with the font files.** Bundling a TTF inside this application *is* redistribution of the font software, so each font's OFL text (`Fonts/LICENSE-*.txt`) lives in the repo and is copied to the deployment output alongside the fonts. Keep it that way.
- **Never sell the font files by themselves** (not applicable to this service, but an OFL condition).
- **Don't modify a font and redistribute it under its Reserved Font Name.** A modified Pinyon Script or Liberation font must be renamed.

### Adding new fonts — read this first

Drop a `.ttf`/`.otf` into `Fonts/` plus its license file and it will be picked up automatically. **But check the license before adding anything:**

- ✅ **SIL OFL fonts** (everything on Google Fonts under OFL): safe for this service.
- ❌ **Adobe Fonts / Creative Cloud fonts** (e.g., Bickham Script): the [Adobe Fonts terms](https://helpx.adobe.com/fonts/using/font-licensing.html) exclude server installation and automated document generation. A CC subscription does **not** cover this service.
- ⚠️ **Purchased desktop licenses** (Fontspring, MyFonts, etc.): standard desktop EULAs cover documents a licensed human creates, not automated server generation. A separate "server", "app", or "electronic document" license tier is required — get it in writing before bundling.

A font file's internal embedding flag (`fsType`) permitting embedding is **not** a license; the EULA governs.

## Postman Example

BODY (raw):
```
{
  "htmlContent": "<h1>Hello World <img src=\"https://placehold.co/60\" width=\"60\" height=\"60\" alt=\"Logo\" style=\"vertical-align: middle; margin-left: 10px;\"></h1><p>Some content here.</p><table border=\"1\"><tr><td>Row1 Col1</td><td>Row1 Col2</td><td>Row1 Col3</td></tr><tr><td>Row2 Col1</td><td>Row2 Col2</td><td>Row2 Col3</td></tr><tr><td>Row3 Col1</td><td>Row3 Col2</td><td>Row3 Col3</td></tr></table><p>This is the ending paragraph</p>",
  "documentTitle": "My Document",
  "documentLanguage": "en-US",
  "pageOrientation": "portrait",
  "marginTop": 10,
  "marginRight": 10,
  "marginBottom": 10,
  "marginLeft": 10,
  "showPageNumbers": true,
  "watermark": "DRAFT",
  "footerContent": "<p style='text-align:center; font-size:8pt;'>Company Confidential</p>"
}
```

## License

See [LICENSE](LICENSE) for details.
