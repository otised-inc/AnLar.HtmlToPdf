# AnLar.HtmlToPdf

An ASP.NET Core Web API that converts HTML content into accessible PDF documents compliant with the **PDF/UA** (Universal Accessibility) standard. Built with [iText](https://itextpdf.com/) for reliable, structured PDF generation suitable for screen readers and assistive technologies.

## Features

- **PDF/UA-1 Compliance** — Tagged PDF structure with XMP metadata, document language, title, and `DisplayDocTitle` viewer preference (PDF 1.7)
- **HTML-to-PDF Conversion** — Accepts raw HTML fragments or full documents and returns a PDF binary
- **Semantic Heading Structure** — Custom tag worker produces clean H1–H6 structure elements without iText's default intermediate `P` wrappers
- **Automatic Bookmarks** — Headings generate a PDF outline/bookmark tree via iText's `OutlineHandler`
- **Bundled Fonts** — Ships with Liberation Serif (Regular, Bold, Italic, Bold Italic) so PDFs render consistently even on minimal Linux containers
- **Cross-Platform Font Resolution** — Multi-strategy font loading: bundled content files, embedded assembly resources, and system font directories on Windows and Linux
- **Smart HTML Wrapping** — Automatically wraps partial HTML snippets in a complete document with `@page` margins, language attribute, and default serif typography
- **Page Layout Control** — Configurable page orientation (portrait/landscape) and per-side margins in millimeters
- **Optional Page Numbers** — Adds centered "Page X of Y" footers, marked as PDF artifacts to preserve accessibility compliance
- **Watermark Support** — Optional diagonal watermark text (e.g. "DRAFT", "CONFIDENTIAL") rendered in light gray with 30% opacity, marked as a PDF artifact so it doesn't interfere with screen readers
- **Custom HTML Footers** — Render arbitrary HTML/CSS as a footer on every page, marked as a PDF artifact to preserve accessibility compliance
- **Inline Image Support** — Handles base64-encoded and URL-referenced images with full 508/PDF-UA compliance: images with `alt` text are tagged as Figure elements, empty `alt=""` marks images as decorative (excluded from structure tree), and missing `alt` attributes receive a fallback description
- **PDF-to-Image Export** — Convert generated PDFs to high-quality PNG images at configurable DPI via the `/pdf/images` endpoint

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

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
  "footerContent": "<p style='text-align:center; font-size:8pt;'>Company Confidential</p>"
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

**Response:** `application/pdf` binary stream.

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

## Project Structure

```
AnLar.HtmlToPdf/
├── AnLar.HtmlToPdf.sln
└── AnLar.HtmlToPdf/
    ├── AnLar.HtmlToPdf.csproj
    ├── Program.cs                        # App entry point & service registration
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── Controllers/
    │   └── PdfController.cs              # POST /pdf endpoint
    ├── Services/
    │   └── AccessiblePdfGenerator.cs     # Core PDF generation & accessibility logic
    ├── DTOs/
    │   ├── PdfRequest.cs                 # Request model
    │   └── PdfImagesResponse.cs          # Response model for /pdf/images
    ├── Properties/
    │   └── launchSettings.json
    └── Fonts/
        ├── LiberationSerif-Regular.ttf
        ├── LiberationSerif-Bold.ttf
        ├── LiberationSerif-Italic.ttf
        ├── LiberationSerif-BoldItalic.ttf
        └── LICENSE-LiberationFonts.txt
AnLar.HtmlToPdf.Tests/
    ├── InlineImageTests.cs            # Unit tests for inline image handling
    ├── FooterTests.cs                 # Unit tests for HTML footer rendering
    └── PdfToImageTests.cs             # Unit tests for PDF-to-image export
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

## Dependencies

| Package                          | Version | Purpose                                   |
|----------------------------------|---------|-------------------------------------------|
| `itext.pdfhtml`                  | 6.3.1   | HTML-to-PDF conversion with iText         |
| `itext.bouncy-castle-adapter`    | 9.5.0   | Cryptography adapter required by iText    |
| `PDFtoImage`                     | 5.2.0   | PDF page rendering for `/pdf/images` endpoint |

Fonts are [Liberation Serif](https://github.com/liberationfonts/liberation-fonts) licensed under the SIL Open Font License.

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
