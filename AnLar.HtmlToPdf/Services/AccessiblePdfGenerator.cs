using AnLar.HtmlToPdf.DTOs;
using iText.Html2pdf;
using iText.Html2pdf.Attach;
using iText.Html2pdf.Attach.Impl;
using iText.Html2pdf.Attach.Impl.Tags;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.XMP;
using iText.Kernel.Geom;
using iText.Layout;
using iText.Layout.Font;
using iText.StyledXmlParser.Node;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AnLar.HtmlToPdf.Services
{
    public class AccessiblePdfGenerator
    {
        private readonly ILogger<AccessiblePdfGenerator> _logger;

        // Cache only the *list* of font file paths (one-time directory enumeration).
        // We deliberately do NOT cache the FontProvider itself — HtmlConverter
        // registers @font-face fonts into the FontProvider as a side effect, and a
        // shared instance accumulates that state across requests, which slows
        // subsequent renders and risks cross-request leakage.
        //
        // Empirically the same trap applies to a shared FontSet (FontProvider's
        // inner registry): @font-face fonts get added to it via the per-request
        // FontProvider wrapper, so requests after the first slow down. Don't share.
        private static IReadOnlyList<string>? _cachedFontFilePaths;
        private static readonly object _fontPathsLock = new();

        // Parsed FontPrograms, by contrast, ARE safe to share: they are immutable
        // after construction and iText itself shares them process-wide via its
        // static FontCache. Caching them here means each request skips re-reading
        // and re-parsing the font files (which happens once per BuildFontProvider
        // call otherwise — twice per request when a footer is stamped). This
        // matters most on Azure App Service, where the content share is
        // network-backed and per-request file reads are expensive.
        private static IReadOnlyList<FontProgram>? _cachedFontPrograms;

        // Tag worker factory has no per-request state — share it.
        private static readonly AccessibleTagWorkerFactory _tagWorkerFactory = new();

        public AccessiblePdfGenerator(ILogger<AccessiblePdfGenerator> logger)
        {
            _logger = logger;
        }

        private IReadOnlyList<string> GetCachedFontFilePaths()
        {
            if (_cachedFontFilePaths != null)
                return _cachedFontFilePaths;

            lock (_fontPathsLock)
            {
                if (_cachedFontFilePaths != null)
                    return _cachedFontFilePaths;

                var paths = new List<string>();

                // Bundled font dir: try known deployment locations and embedded resources.
                var bundledDir = ResolveBundledFontDirectory();
                if (bundledDir != null)
                {
                    paths.AddRange(EnumerateFontFiles(bundledDir));
                    _logger.LogInformation("Cached {Count} bundled fonts from {Dir}", paths.Count, bundledDir);
                }
                else
                {
                    // Fall back to embedded resources (extracted to temp once).
                    var extracted = ExtractEmbeddedFontFiles();
                    paths.AddRange(extracted);
                    _logger.LogInformation("Cached {Count} embedded fonts (extracted to temp)", extracted.Length);
                }

                int bundledCount = paths.Count;

                // EXPERIMENT: skip system font directories. Bundled Liberation Serif
                // covers the default font stack; HTML with @font-face brings its own
                // fonts. System-font lookup matters only if request HTML names a
                // system family explicitly (e.g. "Arial") with no @font-face fallback.

                _logger.LogInformation("Font path cache: {Total} files ({Bundled} bundled, {System} system)",
                    paths.Count, bundledCount, paths.Count - bundledCount);

                _cachedFontFilePaths = paths;
                return _cachedFontFilePaths;
            }
        }

        private IReadOnlyList<FontProgram> GetCachedFontPrograms()
        {
            if (_cachedFontPrograms != null)
                return _cachedFontPrograms;

            var paths = GetCachedFontFilePaths();
            lock (_fontPathsLock)
            {
                if (_cachedFontPrograms != null)
                    return _cachedFontPrograms;

                var programs = new List<FontProgram>(paths.Count);
                foreach (var path in paths)
                {
                    try { programs.Add(FontProgramFactory.CreateFont(path)); }
                    catch { /* skip unreadable/corrupt fonts silently — same as AddDirectory's behavior */ }
                }
                _cachedFontPrograms = programs;
                return _cachedFontPrograms;
            }
        }

        private FontProvider BuildFontProvider()
        {
            // Fresh FontProvider per request (see note above), but fed from the
            // shared FontProgram cache so no font file is re-read or re-parsed.
            var fp = new FontProvider();
            foreach (var program in GetCachedFontPrograms())
                fp.AddFont(program);
            return fp;
        }

        /// <summary>
        /// Creates the font used for stamped overlays (page numbers, watermark).
        /// PDF/UA requires every font used for rendering to be embedded — artifacts
        /// included — so this picks the bundled Liberation Serif face and embeds a
        /// subset, instead of the standard (never-embedded) Helvetica. Falls back
        /// to Helvetica only if no bundled font is available, favoring a rendered
        /// page over a failed request.
        /// </summary>
        private PdfFont CreateOverlayFont(bool bold)
        {
            FontProgram? styleMatch = null;
            foreach (var program in GetCachedFontPrograms())
            {
                var names = program.GetFontNames();
                if (names.IsItalic() || names.IsBold() != bold)
                    continue;
                var psName = names.GetFontName() ?? "";
                if (psName.StartsWith("LiberationSerif", StringComparison.OrdinalIgnoreCase))
                {
                    styleMatch = program;
                    break;
                }
                styleMatch ??= program;
            }

            if (styleMatch != null)
            {
                return PdfFontFactory.CreateFont(styleMatch, PdfEncodings.IDENTITY_H,
                    PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
            }

            _logger.LogWarning("No bundled font available for overlay text; falling back to non-embedded Helvetica");
            return PdfFontFactory.CreateFont(bold ? StandardFonts.HELVETICA_BOLD : StandardFonts.HELVETICA);
        }

        /// <summary>
        /// Pre-warms iText static initialization (CSS parser, default styles, font
        /// scanning) so the first real request doesn't pay that cost.
        /// </summary>
        public void Warmup()
        {
            try
            {
                _ = GetCachedFontFilePaths();
                using var ms = new MemoryStream();
                var props = new ConverterProperties();
                props.SetFontProvider(BuildFontProvider());
                HtmlConverter.ConvertToPdf("<p>warmup</p>", ms, props);

                // Also pre-load the PDFium and SkiaSharp native libraries by
                // rendering + encoding the warmup page at minimal DPI, so the
                // first /pdf/images request doesn't pay that cost.
                var warmupPdf = ms.ToArray();
                foreach (var bitmap in Conversion.ToImages(warmupPdf, options: new RenderOptions { Dpi = 36 }))
                {
                    using (bitmap)
                    using (bitmap.Encode(SKEncodedImageFormat.Png, 100)) { }
                }

                _logger.LogInformation("PDF generator warm-up complete");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDF generator warm-up failed (non-fatal)");
            }
        }

        public byte[] GenerateAccessiblePdfFromHtml(
            string htmlContent,
            string documentTitle,
            string documentLanguage = "en-US",
            string pageOrientation = "portrait",
            float marginTop = 10f,
            float marginRight = 10f,
            float marginBottom = 10f,
            float marginLeft = 10f,
            bool showPageNumbers = false,
            string? watermark = null,
            string? footerContent = null)
        {
            // Pre-size the buffer from the HTML length (rough heuristic, clamped)
            // so large documents don't churn through repeated doubling copies on
            // the large-object heap.
            int initialCapacity = Math.Clamp(htmlContent.Length / 2, 16 * 1024, 4 * 1024 * 1024);
            using var memoryStream = new MemoryStream(initialCapacity);

            var writerProperties = new WriterProperties();
            writerProperties.SetPdfVersion(PdfVersion.PDF_1_7);
            // EXPERIMENT: trade compression for speed. iText defaults to deflate level 9
            // (BEST_COMPRESSION). For an interactive request/response service, BEST_SPEED
            // (level 1) cuts the write phase substantially at modest size cost.
            writerProperties.SetCompressionLevel(iText.Kernel.Pdf.CompressionConstants.BEST_SPEED);

            using (var pdfWriter = new PdfWriter(memoryStream, writerProperties))
            using (var pdfDocument = new PdfDocument(pdfWriter))
            {
                // Keep the MemoryStream open after HtmlConverter closes the writer,
                // so the stamping pass below can wrap the underlying buffer directly
                // instead of paying ToArray()'s full-size copy.
                pdfWriter.SetCloseStream(false);

                pdfDocument.SetTagged();
                pdfDocument.GetCatalog().SetLang(new PdfString(documentLanguage));
                pdfDocument.GetCatalog().SetViewerPreferences(
                    new PdfViewerPreferences().SetDisplayDocTitle(true));

                var documentInfo = pdfDocument.GetDocumentInfo();
                documentInfo.SetTitle(documentTitle);

                SetPdfUaXmpMetadata(pdfDocument, documentTitle);

                var fontProvider = BuildFontProvider();

                var converterProperties = new ConverterProperties();
                converterProperties.SetFontProvider(fontProvider);
                converterProperties.SetTagWorkerFactory(_tagWorkerFactory);
                converterProperties.SetOutlineHandler(OutlineHandler.CreateStandardHandler());

                // Ensure bottom margin has room for footer content
                bool hasFooter = !string.IsNullOrWhiteSpace(footerContent);
                if (hasFooter && marginBottom < 20f)
                    marginBottom = 20f;

                var wrappedHtml = WrapInHtmlDocument(htmlContent, documentTitle, documentLanguage, pageOrientation, marginTop, marginRight, marginBottom, marginLeft);

                HtmlConverter.ConvertToPdf(wrappedHtml, pdfDocument, converterProperties);
                // HtmlConverter closes the PdfDocument after conversion
            }

            bool needsStamping = showPageNumbers || !string.IsNullOrWhiteSpace(watermark) || !string.IsNullOrWhiteSpace(footerContent);

            if (!needsStamping)
                return memoryStream.ToArray();

            // Build a fresh FontProvider for the footer pass (fast — paths are cached).
            ConverterProperties? footerConverterProperties = null;
            if (!string.IsNullOrWhiteSpace(footerContent))
            {
                footerConverterProperties = new ConverterProperties();
                footerConverterProperties.SetFontProvider(BuildFontProvider());
            }

            // Second pass: stamp page numbers, watermark, and/or footer onto the already-generated PDF.
            // Wrap the first pass's live buffer directly — zero copies of the input.
            int firstPassLength = (int)memoryStream.Length;
            using var inputStream = new MemoryStream(memoryStream.GetBuffer(), 0, firstPassLength, writable: false);
            using var outputStream = new MemoryStream(capacity: firstPassLength + 4096);
            // Match the first pass's speed-over-compression trade-off; the default
            // writer would re-deflate new content streams at a slower level.
            var stampWriterProperties = new WriterProperties()
                .SetCompressionLevel(iText.Kernel.Pdf.CompressionConstants.BEST_SPEED);
            using (var reader = new PdfReader(inputStream))
            using (var stampWriter = new PdfWriter(outputStream, stampWriterProperties))
            using (var stampDoc = new PdfDocument(reader, stampWriter))
            {
                if (!string.IsNullOrWhiteSpace(watermark))
                    AddWatermark(stampDoc, watermark!);
                if (!string.IsNullOrWhiteSpace(footerContent))
                    AddFooter(stampDoc, footerContent!, footerConverterProperties!);
                if (showPageNumbers)
                    AddPageNumbers(stampDoc);
            }

            return outputStream.ToArray();
        }

        public PdfImagesResponse GeneratePdfPageImages(
            string htmlContent,
            string documentTitle,
            string documentLanguage = "en-US",
            string pageOrientation = "portrait",
            float marginTop = 10f,
            float marginRight = 10f,
            float marginBottom = 10f,
            float marginLeft = 10f,
            bool showPageNumbers = false,
            string? watermark = null,
            int dpi = 300)
        {
            var pdfBytes = GenerateAccessiblePdfFromHtml(
                htmlContent, documentTitle, documentLanguage, pageOrientation,
                marginTop, marginRight, marginBottom, marginLeft,
                showPageNumbers, watermark);

            var renderOptions = new RenderOptions { Dpi = dpi };

            int pageCount = Conversion.GetPageCount(pdfBytes);
            var encodedPages = new string[pageCount];

            // Bound the number of bitmaps in flight at once. A single decoded letter
            // page at 300 DPI is ~33MB; a long job (e.g. 2000 pages on a 7GB Azure
            // App Service) would OOM if we queued every encode task immediately.
            // Cap concurrency to a small multiple of cores — encoding is CPU-bound,
            // so going wider than this doesn't help and only inflates memory.
            int concurrency = Math.Max(2, Math.Min(Environment.ProcessorCount, 4));
            using var encodeSlot = new SemaphoreSlim(concurrency, concurrency);

            // PDFium is single-threaded (PDFtoImage takes an internal lock around
            // each call), so we render serially. PNG encoding, however, is CPU-heavy
            // and thread-safe per bitmap — offload it to background tasks so it
            // overlaps the next page's render. The semaphore makes the render loop
            // block (and stop pulling the next bitmap from PDFium) once `concurrency`
            // bitmaps are in flight, capping peak memory.
            var encodeTasks = new List<Task>(pageCount);
            int pageIndex = 0;
            foreach (var bitmap in Conversion.ToImages(pdfBytes, options: renderOptions))
            {
                encodeSlot.Wait();
                int idx = pageIndex++;
                var bmp = bitmap; // capture per-iteration
                encodeTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        using (bmp)
                        using (var encoded = bmp.Encode(SKEncodedImageFormat.Png, 100))
                        {
                            // SKData.AsSpan exposes the native PNG buffer with no managed
                            // copy — saves one byte[] allocation per page.
                            encodedPages[idx] = Convert.ToBase64String(encoded.AsSpan());
                        }
                    }
                    finally { encodeSlot.Release(); }
                }));
            }

            Task.WaitAll(encodeTasks.ToArray());

            return new PdfImagesResponse
            {
                PageCount = pageCount,
                Pages = [.. encodedPages]
            };
        }

        /// <summary>
        /// Streams one PNG per page as it finishes encoding. Memory peaks at roughly
        /// (concurrency × bitmap size) + (channel-buffered base64 strings), regardless
        /// of total page count — so a 2000-page job runs in the same footprint as an
        /// 8-page one. Caller is responsible for writing each <see cref="PageImage"/>
        /// to the wire (e.g. NDJSON) before the next one buffers up.
        /// </summary>
        public async IAsyncEnumerable<PageImage> GeneratePdfPageImagesStreamAsync(
            string htmlContent,
            string documentTitle,
            string documentLanguage = "en-US",
            string pageOrientation = "portrait",
            float marginTop = 10f,
            float marginRight = 10f,
            float marginBottom = 10f,
            float marginLeft = 10f,
            bool showPageNumbers = false,
            string? watermark = null,
            int dpi = 300,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var pdfBytes = GenerateAccessiblePdfFromHtml(
                htmlContent, documentTitle, documentLanguage, pageOrientation,
                marginTop, marginRight, marginBottom, marginLeft,
                showPageNumbers, watermark);

            var renderOptions = new RenderOptions { Dpi = dpi };
            int pageCount = Conversion.GetPageCount(pdfBytes);

            int concurrency = Math.Max(2, Math.Min(Environment.ProcessorCount, 4));
            var encodeSlot = new SemaphoreSlim(concurrency, concurrency);

            // Bounded channel applies back-pressure: if the HTTP consumer is slow,
            // encoders block on WriteAsync, which lets the semaphore stay full and
            // halts the renderer. Capacity is small (= concurrency) on purpose so
            // base64 strings don't pile up.
            var channel = Channel.CreateBounded<PageImage>(new BoundedChannelOptions(concurrency)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

            var producer = Task.Run(async () =>
            {
                Exception? error = null;
                var encodeTasks = new List<Task>(Math.Min(pageCount, 64));
                try
                {
                    int idx = 0;
                    foreach (var bitmap in Conversion.ToImages(pdfBytes, options: renderOptions))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await encodeSlot.WaitAsync(cancellationToken).ConfigureAwait(false);

                        int page = idx++;
                        var bmp = bitmap; // capture per-iteration
                        encodeTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                using (bmp)
                                using (var encoded = bmp.Encode(SKEncodedImageFormat.Png, 100))
                                {
                                    var b64 = Convert.ToBase64String(encoded.AsSpan());
                                    await channel.Writer.WriteAsync(
                                        new PageImage(page, pageCount, b64),
                                        cancellationToken).ConfigureAwait(false);
                                }
                            }
                            finally { encodeSlot.Release(); }
                        }, cancellationToken));
                    }
                    await Task.WhenAll(encodeTasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                channel.Writer.TryComplete(error);
            }, cancellationToken);

            try
            {
                await foreach (var img in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    yield return img;
            }
            finally
            {
                // Drain the producer before disposing the semaphore. Swallow the
                // cancellation/disconnect path — those are normal client behavior.
                try { await producer.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogWarning(ex, "Streaming page-image producer faulted"); }
                encodeSlot.Dispose();
            }
        }

        private void AddPageNumbers(PdfDocument pdfDocument)
        {
            int totalPages = pdfDocument.GetNumberOfPages();
            var font = CreateOverlayFont(bold: false);
            const float fontSize = 9f;

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageSize = page.GetPageSize();
                var text = $"Page {i} of {totalPages}";
                float textWidth = font.GetWidth(text, fontSize);
                float x = (pageSize.GetWidth() - textWidth) / 2f;

                var canvas = new PdfCanvas(page);
                // Mark as artifact so screen readers and PDF/UA validators ignore it
                canvas.BeginMarkedContent(PdfName.Artifact);
                canvas.SetFontAndSize(font, fontSize)
                      .BeginText()
                      .MoveText(x, 10f)
                      .ShowText(text)
                      .EndText();
                canvas.EndMarkedContent();
                canvas.Release();
            }
        }

        private static void AddFooter(PdfDocument pdfDocument, string footerHtml, ConverterProperties converterProperties)
        {
            int totalPages = pdfDocument.GetNumberOfPages();

            // {totalPages} is the same on every page — substitute it once up front.
            // Only {pageNumber} forces a per-page HTML conversion; footers using
            // just {totalPages} (or no placeholders) share one converted element tree.
            if (footerHtml.Contains("{totalPages}"))
                footerHtml = footerHtml.Replace("{totalPages}", totalPages.ToString());
            bool hasPlaceholders = footerHtml.Contains("{pageNumber}");

            // If no per-page placeholders, convert once and reuse for all pages
            var sharedElements = hasPlaceholders ? null : HtmlConverter.ConvertToElements(footerHtml, converterProperties);

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageSize = page.GetPageSize();

                // Footer rectangle: full width with 36pt (0.5in) horizontal margins,
                // positioned at the bottom of the page in the margin area.
                const float horizontalMargin = 36f;
                const float footerHeight = 50f;
                const float footerBottom = 10f;
                var footerRect = new Rectangle(
                    pageSize.GetLeft() + horizontalMargin,
                    pageSize.GetBottom() + footerBottom,
                    pageSize.GetWidth() - 2 * horizontalMargin,
                    footerHeight);

                // Replace the per-page placeholder if present
                var elements = sharedElements ?? HtmlConverter.ConvertToElements(
                    footerHtml.Replace("{pageNumber}", i.ToString()),
                    converterProperties);

                var pdfCanvas = new PdfCanvas(page);
                // Mark as artifact so screen readers and PDF/UA validators ignore it
                pdfCanvas.BeginMarkedContent(PdfName.Artifact);

                using (var layoutCanvas = new Canvas(pdfCanvas, footerRect))
                {
                    foreach (var element in elements)
                    {
                        if (element is iText.Layout.Element.IBlockElement blockElement)
                            layoutCanvas.Add(blockElement);
                        else if (element is iText.Layout.Element.Image image)
                            layoutCanvas.Add(image);
                    }
                }

                pdfCanvas.EndMarkedContent();
                pdfCanvas.Release();
            }
        }

        private void AddWatermark(PdfDocument pdfDocument, string watermarkText)
        {
            int totalPages = pdfDocument.GetNumberOfPages();
            var font = CreateOverlayFont(bold: true);
            const float fontSize = 60f;
            var grayColor = new DeviceRgb(200, 200, 200);
            var gs = new PdfExtGState().SetFillOpacity(0.3f);

            // Text width and rotation are page-independent — compute them once.
            float textWidth = font.GetWidth(watermarkText, fontSize);
            float cos = (float)Math.Cos(Math.PI / 4);
            float sin = (float)Math.Sin(Math.PI / 4);

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageSize = page.GetPageSize();
                float centerX = pageSize.GetWidth() / 2f;
                float centerY = pageSize.GetHeight() / 2f;

                // Offset by half the text width so the watermark is visually centered
                float x = centerX - (textWidth / 2f) * cos;
                float y = centerY - (textWidth / 2f) * sin;

                var canvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdfDocument);
                canvas.SaveState();
                canvas.SetExtGState(gs);
                // Mark as artifact so screen readers ignore the watermark
                canvas.BeginMarkedContent(PdfName.Artifact);
                canvas.SetFontAndSize(font, fontSize)
                      .SetColor(grayColor, true)
                      .BeginText()
                      .SetTextMatrix(cos, sin, -sin, cos, x, y)
                      .ShowText(watermarkText)
                      .EndText();
                canvas.EndMarkedContent();
                canvas.RestoreState();
                canvas.Release();
            }
        }

        private string? ResolveBundledFontDirectory()
        {
            var assembly = typeof(AccessiblePdfGenerator).Assembly;
            var assemblyDir = System.IO.Path.GetDirectoryName(assembly.Location) ?? "";
            string[] candidateDirs =
            [
                System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts"),
                System.IO.Path.Combine(assemblyDir, "Fonts"),
                System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Fonts"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts"),
                // Azure Web App common paths
                "/home/site/wwwroot/Fonts",
            ];

            foreach (var dir in candidateDirs)
            {
                if (!Directory.Exists(dir))
                    continue;
                if (EnumerateFontFiles(dir).Count > 0)
                    return dir;
            }
            return null;
        }

        // Linux filesystems are case-sensitive, so Directory.GetFiles(dir, "*.ttf")
        // would miss .TTF / .Ttf etc. Enumerate once and filter case-insensitively.
        private static readonly string[] FontExtensions = [".ttf", ".otf"];
        private static IReadOnlyList<string> EnumerateFontFiles(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir)
                    .Where(f => FontExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
            }
            catch (DirectoryNotFoundException) { return []; }
            catch (UnauthorizedAccessException) { return []; }
        }

        private string[] ExtractEmbeddedFontFiles()
        {
            var assembly = typeof(AccessiblePdfGenerator).Assembly;
            var ttfResources = assembly.GetManifestResourceNames()
                .Where(r => r.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (ttfResources.Length == 0)
                return [];

            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AnLar.HtmlToPdf.Fonts");
            Directory.CreateDirectory(tempDir);

            var paths = new List<string>(ttfResources.Length);
            foreach (var resourceName in ttfResources)
            {
                var targetPath = System.IO.Path.Combine(tempDir, resourceName);
                if (!File.Exists(targetPath))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null) continue;
                    using var fs = File.Create(targetPath);
                    stream.CopyTo(fs);
                }
                paths.Add(targetPath);
            }
            return [.. paths];
        }

        private static string[] GetSystemFontDirectories()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                return string.IsNullOrEmpty(winFonts) ? [] : [winFonts];
            }

            // Linux font directories (Azure Web Apps, containers, etc.)
            string[] linuxCandidates = ["/usr/share/fonts", "/usr/local/share/fonts", "/home/.fonts"];
            var existing = new System.Collections.Generic.List<string>();
            foreach (var dir in linuxCandidates)
            {
                if (Directory.Exists(dir))
                    existing.Add(dir);
            }
            return [.. existing];
        }

        private static void SetPdfUaXmpMetadata(PdfDocument pdfDocument, string documentTitle)
        {
            XMPMeta xmpMeta = pdfDocument.GetXmpMetadata() ?? XMPMetaFactory.Create();

            const string pdfUaIdSchema = "http://www.aiim.org/pdfua/ns/id/";
            const string pdfUaIdPrefix = "pdfuaid";
            XMPMetaFactory.GetSchemaRegistry().RegisterNamespace(pdfUaIdSchema, pdfUaIdPrefix);

            xmpMeta.SetPropertyInteger(pdfUaIdSchema, "part", 1);

            const string dcSchema = "http://purl.org/dc/elements/1.1/";
            xmpMeta.SetLocalizedText(dcSchema, "title", "x-default", "x-default", documentTitle);

            pdfDocument.SetXmpMetadata(xmpMeta);
        }

        private static string WrapInHtmlDocument(
            string htmlContent,
            string documentTitle,
            string documentLanguage,
            string pageOrientation,
            float marginTop,
            float marginRight,
            float marginBottom,
            float marginLeft)
        {
            // Span-based prefix check — TrimStart() on a string would copy the
            // whole (potentially multi-MB) HTML document just to inspect the start.
            var trimmed = htmlContent.AsSpan().TrimStart();
            if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return htmlContent;
            }

            var orientation = pageOrientation.Equals("landscape", StringComparison.OrdinalIgnoreCase)
                ? "landscape"
                : "portrait";

            return $@"<!DOCTYPE html>
<html lang=""{documentLanguage}"">
<head>
    <meta charset=""UTF-8""/>
    <title>{System.Net.WebUtility.HtmlEncode(documentTitle)}</title>
    <style>
        @page {{
            size: {orientation};
            margin: {marginTop}mm {marginRight}mm {marginBottom}mm {marginLeft}mm;
        }}
        img {{
            max-width: 100%;
            height: auto;
        }}
    </style>
</head>
<body style=""font-family: 'Liberation Serif', 'Times New Roman', Times, serif; font-size: 16px;"">
{htmlContent}
</body>
</html>";
        }

        /// <summary>
        /// Custom tag worker factory that overrides heading processing to avoid
        /// iText creating P structure elements inside H1-H6 headings.
        /// </summary>
        private class AccessibleTagWorkerFactory : DefaultTagWorkerFactory
        {
            public override ITagWorker GetCustomTagWorker(IElementNode tag, ProcessorContext context)
            {
                string name = tag.Name();
                if (name == "h1" || name == "h2" || name == "h3" ||
                    name == "h4" || name == "h5" || name == "h6")
                {
                    return new AccessibleHeadingTagWorker(tag, context);
                }
                if (name == "img")
                {
                    return new AccessibleImageTagWorker(tag, context);
                }
                return null;
            }
        }

        /// <summary>
        /// Processes heading tags as Paragraph elements (like PTagWorker) but sets
        /// the PDF structure role to H1-H6. This produces a single heading structure
        /// element without an intermediate P child.
        /// </summary>
        private class AccessibleHeadingTagWorker : PTagWorker
        {
            private readonly string headingRole;

            public AccessibleHeadingTagWorker(IElementNode element, ProcessorContext context)
                : base(element, context)
            {
                headingRole = element.Name().ToUpperInvariant();
            }

            public override IPropertyContainer GetElementResult()
            {
                var result = base.GetElementResult();
                if (result is iText.Layout.Element.Paragraph paragraph)
                {
                    paragraph.GetAccessibilityProperties().SetRole(headingRole);
                }
                return result;
            }
        }

        /// <summary>
        /// Processes img tags to ensure 508/PDF-UA compliance for inline images.
        /// Sets the Figure role and alternate description from the HTML alt attribute.
        /// Decorative images (alt="") are excluded from the structure tree.
        /// Images without an alt attribute receive a fallback description.
        /// </summary>
        private class AccessibleImageTagWorker : ImgTagWorker
        {
            private readonly IElementNode _element;

            public AccessibleImageTagWorker(IElementNode element, ProcessorContext context)
                : base(element, context)
            {
                _element = element;
            }

            public override IPropertyContainer GetElementResult()
            {
                var result = base.GetElementResult();

                if (result is iText.Layout.Element.Image image)
                {
                    string? altText = _element.GetAttribute("alt");
                    var accessibilityProperties = image.GetAccessibilityProperties();

                    if (altText != null && altText.Length == 0)
                    {
                        // Empty alt="" indicates a decorative image.
                        // For PDF/UA, decorative content should not appear in the
                        // structure tree. Setting the role to null marks it as an artifact.
                        accessibilityProperties.SetRole(null);
                    }
                    else if (!string.IsNullOrEmpty(altText))
                    {
                        // Meaningful image with alt text
                        accessibilityProperties.SetRole(StandardRoles.FIGURE);
                        accessibilityProperties.SetAlternateDescription(altText);
                    }
                    else
                    {
                        // No alt attribute — set Figure role with fallback description
                        // to maintain 508 compliance
                        accessibilityProperties.SetRole(StandardRoles.FIGURE);
                        accessibilityProperties.SetAlternateDescription("Image");
                    }
                }

                return result;
            }
        }
    }
}
