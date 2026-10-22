using ERP.MES.Domain.Documents;

namespace ERP.MES.Domain.Tests.Requests;

// The request PDF rules (Decision Log #35, #54, #87): PDF only, exact size limit, a usable file name, SHA-256 integrity hash.
public class StoredDocumentTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static bool Create(string? name, byte[]? content, long max, out StoredDocument? document, out PdfError error) =>
        StoredDocument.TryCreateRequestPdf(name, content, RequestFixtures.Hash(content ?? []), max, 7, Now, out document, out error);

    [Fact]
    public void A_valid_pdf_is_accepted_with_its_hash_size_and_uploader()
    {
        var content = RequestFixtures.PdfBytes(120);

        Assert.True(Create("Order 1.PDF", content, 1000, out var document, out var error));

        Assert.Equal(PdfError.None, error);
        Assert.Equal("Order 1.PDF", document!.FileName);
        Assert.Equal(DocumentType.RequestPdf, document.Type);
        Assert.Equal(120, document.ContentLength);
        Assert.Equal(7, document.UploadedByUserId);
        Assert.Equal(Now, document.UploadedAtUtc);
        Assert.Equal(RequestFixtures.Hash(content), document.ContentHash);
        Assert.Equal(content, document.Content);
    }

    [Theory]
    [InlineData(@"C:\Users\x\Documents\a.pdf", "a.pdf")]
    [InlineData("/home/x/a.pdf", "a.pdf")]
    [InlineData("  a b.pdf  ", "a b.pdf")]
    public void Only_the_file_name_is_kept(string given, string expected)
    {
        Assert.True(Create(given, RequestFixtures.PdfBytes(), 1000, out var document, out _));
        Assert.Equal(expected, document!.FileName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a.txt")]
    [InlineData("a.pdf.exe")]
    [InlineData(".pdf")]
    [InlineData("folder/")]
    public void A_missing_or_non_pdf_name_is_refused(string? name)
    {
        Assert.False(Create(name, RequestFixtures.PdfBytes(), 1000, out var document, out var error));
        Assert.Null(document);
        Assert.Equal(PdfError.FileNameInvalid, error);
    }

    [Fact]
    public void An_over_long_file_name_is_refused()
    {
        Assert.False(Create(new string('a', 252) + ".pdf", RequestFixtures.PdfBytes(), 1000, out _, out var error));
        Assert.Equal(PdfError.FileNameInvalid, error);
        Assert.True(Create(new string('a', 251) + ".pdf", RequestFixtures.PdfBytes(), 1000, out _, out _));
    }

    [Fact]
    public void Empty_content_is_refused()
    {
        Assert.False(Create("a.pdf", [], 1000, out _, out var error));
        Assert.Equal(PdfError.Empty, error);
        Assert.False(Create("a.pdf", null, 1000, out _, out error));
        Assert.Equal(PdfError.Empty, error);
    }

    [Fact]
    public void The_size_limit_is_inclusive_one_byte_more_is_refused()
    {
        Assert.True(Create("a.pdf", RequestFixtures.PdfBytes(100), 100, out _, out _));
        Assert.False(Create("a.pdf", RequestFixtures.PdfBytes(101), 100, out _, out var error));
        Assert.Equal(PdfError.TooLarge, error);
    }

    [Fact]
    public void The_content_must_carry_the_pdf_signature_not_just_the_extension()
    {
        Assert.False(Create("a.pdf", System.Text.Encoding.ASCII.GetBytes("MZ this is an executable pretending to be a pdf"), 1000, out _, out var error));
        Assert.Equal(PdfError.NotPdf, error);

        // A short preamble before the signature is allowed by the PDF format.
        var withPreamble = System.Text.Encoding.ASCII.GetBytes("\r\n\r\n%PDF-1.7\n");
        Assert.True(Create("a.pdf", withPreamble, 1000, out _, out _));

        // But the signature must be near the start.
        var late = new byte[2000];
        Array.Fill(late, (byte)' ');
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(late, 1500);
        Assert.False(Create("a.pdf", late, 5000, out _, out error));
        Assert.Equal(PdfError.NotPdf, error);
    }

    [Fact]
    public void The_upload_time_must_be_utc()
    {
        Assert.Throws<ArgumentException>(() => StoredDocument.TryCreateRequestPdf("a.pdf", RequestFixtures.PdfBytes(), RequestFixtures.Hash(RequestFixtures.PdfBytes()), 1000, 7, DateTime.SpecifyKind(Now, DateTimeKind.Local), out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ZZZZ000000000000000000000000000000000000000000000000000000000000")]
    public void A_malformed_content_hash_is_a_programming_error(string hash)
    {
        Assert.Throws<ArgumentException>(() => StoredDocument.TryCreateRequestPdf("a.pdf", RequestFixtures.PdfBytes(), hash, 1000, 7, Now, out _, out _));
    }
}
