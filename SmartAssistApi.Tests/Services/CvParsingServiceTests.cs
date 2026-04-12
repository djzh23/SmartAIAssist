using SmartAssistApi.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SmartAssistApi.Tests.Services;

public class CvParsingServiceTests
{
    private readonly CvParsingService _service = new();

    private static byte[] BuildPdfWithText(string text)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(50, 750), font);
        return builder.Build();
    }

    [Fact]
    public void ExtractTextFromPdf_WithValidPdf_ReturnsText()
    {
        var bytes = BuildPdfWithText("Hello Lebenslauf Test");
        var b64 = Convert.ToBase64String(bytes);

        var text = _service.ExtractTextFromPdf(b64);

        Assert.Contains("Hello", text, StringComparison.Ordinal);
        Assert.Contains("Lebenslauf", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractTextFromPdf_WithDataUrlPrefix_StillExtracts()
    {
        var bytes = BuildPdfWithText("DataUrlPrefixOk");
        var b64 = "data:application/pdf;base64," + Convert.ToBase64String(bytes);

        var text = _service.ExtractTextFromPdf(b64);

        Assert.Contains("DataUrlPrefixOk", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractTextFromPdf_TruncatesAt3000Chars()
    {
        var longBody = new string('x', 5000);
        var bytes = BuildPdfWithText(longBody);
        var b64 = Convert.ToBase64String(bytes);

        var text = _service.ExtractTextFromPdf(b64);

        Assert.True(text.Length <= 3000, $"expected <= 3000, got {text.Length}");
    }

    [Fact]
    public void ExtractTextFromPdf_WithInvalidBase64_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _service.ExtractTextFromPdf("not-valid-base64!!!"));
    }
}
