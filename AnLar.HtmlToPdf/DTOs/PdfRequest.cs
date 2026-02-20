namespace AnLar.HtmlToPdf.DTOs
{
    public class PdfRequest
    {
        public string HtmlContent { get; set; } = string.Empty;
        public string? DocumentTitle { get; set; }
        public string? DocumentLanguage { get; set; }
    }
}
