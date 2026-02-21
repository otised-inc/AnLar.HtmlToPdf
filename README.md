# AnLar.HtmlToPdf

An ASP.NET Core Web API that converts HTML content into accessible PDF documents compliant with the PDF/UA standard. Built with [iText 7](https://itextpdf.com/) for reliable, structured PDF generation suitable for screen readers and assistive technologies.

## Features

- **PDF/UA Compliance** — Tagged PDF structure with XMP metadata, document language, and title for accessibility
- **HTML-to-PDF Conversion** — Accepts raw HTML (fragments or full documents) and returns a PDF binary
- **Semantic Heading Structure** — Custom tag worker ensures proper H1–H6 hierarchy without intermediate wrapper elements
- **Cross-Platform Font Support** — Resolves system fonts on both Windows and Linux
- **Smart HTML Wrapping** — Automatically wraps partial HTML snippets in a complete document structure with sensible defaults

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

| Field              | Type   | Required | Description                          |
|--------------------|--------|----------|--------------------------------------|
| `htmlContent`      | string | Yes      | HTML content to convert              |
| `documentTitle`    | string | No       | Title embedded in PDF metadata       |
| `documentLanguage` | string | No       | Language tag (e.g. `en-US`, `nl-NL`) |

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
├── Controllers/
│   └── PdfController.cs              # POST /pdf endpoint
├── Services/
│   └── AccessiblePdfGenerator.cs     # Core PDF generation & accessibility logic
├── DTOs/
│   └── PdfRequest.cs                 # Request model
└── Program.cs                        # App entry point & service registration
```

## Build & Publish

```bash
# Build for release
dotnet build -c Release

# Publish for deployment
dotnet publish -c Release
```

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
