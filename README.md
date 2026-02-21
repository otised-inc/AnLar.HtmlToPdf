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
  "documentLanguage": "en-US"
}
```

| Field              | Type   | Required | Default      | Description                                  |
|--------------------|--------|----------|--------------|----------------------------------------------|
| `htmlContent`      | string | Yes      | —            | HTML content to convert                      |
| `documentTitle`    | string | No       | `"Untitled"` | Title embedded in PDF metadata               |
| `documentLanguage` | string | No       | `"en-US"`    | BCP 47 language tag (e.g. `en-US`, `nl-NL`) |

**Response:** `application/pdf` binary stream.

**Example (curl):**

```bash
curl -X POST https://localhost:50670/pdf \
  -H "Content-Type: application/json" \
  -d '{"htmlContent":"<h1>Report</h1><p>Content here.</p>","documentTitle":"Report","documentLanguage":"en-US"}' \
  -o output.pdf
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
    │   └── PdfRequest.cs                 # Request model
    ├── Properties/
    │   └── launchSettings.json
    └── Fonts/
        ├── LiberationSerif-Regular.ttf
        ├── LiberationSerif-Bold.ttf
        ├── LiberationSerif-Italic.ttf
        ├── LiberationSerif-BoldItalic.ttf
        └── LICENSE-LiberationFonts.txt
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

Fonts are [Liberation Serif](https://github.com/liberationfonts/liberation-fonts) licensed under the SIL Open Font License.

## Postman Example

POST: https://htmltopdfdevlinux.azurewebsites.net/pdf

BODY (raw):
```
{
  "htmlContent": "<h1>Hello World</h1><p>Some content here.</p>",
  "documentTitle": "My Document",
  "documentLanguage": "en-US"
}
```

## License

See [LICENSE](LICENSE) for details.
