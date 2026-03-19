namespace AnLar.HtmlToPdf.DTOs
{
    public class PdfImagesResponse
    {
        /// <summary>
        /// The total number of pages in the generated PDF.
        /// </summary>
        public int PageCount { get; set; }

        /// <summary>
        /// Base64-encoded PNG image for each page, in page order.
        /// </summary>
        public List<string> Pages { get; set; } = new();
    }
}
