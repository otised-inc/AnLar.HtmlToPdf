using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    /// <summary>
    /// End-to-end coverage for gzip-compressed request bodies. Callers ship
    /// large HTML documents (hundreds of KB), so the endpoints accept
    /// <c>Content-Encoding: gzip</c> via ASP.NET Core's request-decompression
    /// middleware — no separate endpoints, and plain JSON keeps working.
    /// </summary>
    public class RequestDecompressionTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RequestDecompressionTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private static string PdfRequestJson =>
            "{\"htmlContent\":\"<h1>Compressed</h1><p>Body sent gzipped.</p>\",\"documentTitle\":\"Gzip Test\"}";

        private static ByteArrayContent GzipJsonContent(string json)
        {
            using var ms = new MemoryStream();
            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                gzip.Write(bytes, 0, bytes.Length);
            }
            var content = new ByteArrayContent(ms.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            return content;
        }

        [Fact]
        public async Task Pdf_GzippedRequestBody_ReturnsPdf()
        {
            using var client = _factory.CreateClient();

            using var content = GzipJsonContent(PdfRequestJson);
            using var resp = await client.PostAsync("/pdf", content);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            // %PDF magic
            Assert.True(bytes.Length > 4, "PDF should have content");
            Assert.Equal((byte)'%', bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'D', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
        }

        [Fact]
        public async Task Pdf_PlainJsonRequestBody_StillWorks()
        {
            using var client = _factory.CreateClient();

            using var content = new StringContent(PdfRequestJson, Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync("/pdf", content);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            Assert.Equal((byte)'%', bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
        }

        [Fact]
        public async Task PdfImages_GzippedRequestBody_ReturnsPages()
        {
            using var client = _factory.CreateClient();

            var json = "{\"htmlContent\":\"<h1>Images</h1>\",\"documentTitle\":\"Gzip Images\",\"dpi\":72}";
            using var content = GzipJsonContent(json);
            using var resp = await client.PostAsync("/pdf/images", content);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = JObject.Parse(await resp.Content.ReadAsStringAsync());
            Assert.True((int)body["pageCount"]! >= 1);
        }
    }
}
