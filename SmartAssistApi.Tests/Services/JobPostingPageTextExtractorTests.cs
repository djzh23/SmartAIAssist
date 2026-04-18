using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class JobPostingPageTextExtractorTests
{
    [Fact]
    public void FromHtml_PrefersJsonLdJobPosting_AndDropsCssAndSchemaNoise()
    {
        var html = """
            <html><head>
            <style>@font-face { font-family: 'XING Sans'; src: url(https://static.xingcdn.com/x.woff2); }</style>
            <script type="application/ld+json">
            {"@context":"https://schema.org/","@type":"JobPosting","title":"Performance Marketing Manager (m/w/d)","description":"<p>VR-Bank Ludwigsburg. Eine Bank für alle. Mit einem Bilanzvolumen von 6 Milliarden Euro und rund 168.000 Kunden ist die VR-Bank Ludwigsburg eine der großen Genossenschaftsbanken in Baden-Württemberg. Marketing ist heute ein Motor für unseren Vertrieb.</p>"}
            </script>
            </head><body>XING junk title | noise</body></html>
            """;
        var t = JobPostingPageTextExtractor.FromHtml(html);

        Assert.Contains("Performance Marketing Manager", t);
        Assert.Contains("VR-Bank Ludwigsburg", t);
        Assert.DoesNotContain("@font-face", t);
        Assert.DoesNotContain("xingcdn.com", t);
        Assert.DoesNotContain("@context", t);
    }

    [Fact]
    public void FromHtml_WithoutJsonLd_RemovesStyleBlocksThenStripsTags()
    {
        var html = """
            <html><head><style>body{font-family:X} @import url(x.css);</style></head>
            <body><main><p>This is a visible job posting paragraph with enough characters for meaningful extraction
            and no JSON-LD block on this test page at all.</p></main></body></html>
            """;
        var t = JobPostingPageTextExtractor.FromHtml(html);

        Assert.Contains("visible job posting paragraph", t);
        Assert.DoesNotContain("@import", t);
        Assert.DoesNotContain("font-family", t);
    }
}
