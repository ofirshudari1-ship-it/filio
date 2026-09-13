using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FileFox.Services;
using Xunit;

namespace FileFox.Tests;

/// <summary>
/// בודק OCR אמיתי מקצה לקצה - לא מדמה (mock) את מנוע Tesseract, אלא מצייר תמונת PNG אמיתית
/// עם טקסט ומריץ עליה את אותו קוד שרץ בפועל באפליקציה. זו הדרך היחידה לוודא שנתוני השפה
/// (tessdata) ושהספרייה הילידית (native) באמת מותקנות ועובדות, לא רק שהקוד מתקמפל.
/// </summary>
public class ImageTextServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "FileFoxOcrTests_" + Guid.NewGuid().ToString("N"));

    public ImageTextServiceTests() => Directory.CreateDirectory(_sandbox);

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    private string CreateTextImage(string text)
    {
        var path = Path.Combine(_sandbox, "ocr-test.png");
        using var bitmap = new Bitmap(700, 140);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            using var font = new Font("Arial", 28, System.Drawing.FontStyle.Bold);
            g.DrawString(text, font, Brushes.Black, new PointF(15, 45));
        }
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void ExtractText_RecognizesTextRenderedIntoAnImage()
    {
        var imagePath = CreateTextImage("INVOICE 48213");

        var text = ImageTextService.ExtractText(imagePath);

        Assert.Contains("INVOICE", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("48213", text);
    }

    [Fact]
    public void ExtractText_ReturnsEmptyForUnsupportedExtension()
    {
        var textFile = Path.Combine(_sandbox, "notes.txt");
        File.WriteAllText(textFile, "INVOICE 48213");

        Assert.Equal(string.Empty, ImageTextService.ExtractText(textFile));
    }

    [Fact]
    public void ExtractText_ReturnsEmptyInsteadOfThrowingForCorruptImage()
    {
        var corruptPath = Path.Combine(_sandbox, "corrupt.png");
        File.WriteAllText(corruptPath, "this is not a real png file");

        var text = ImageTextService.ExtractText(corruptPath);

        Assert.Equal(string.Empty, text);
    }

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("scan.png", true)]
    [InlineData("document.pdf", false)]
    [InlineData("archive.zip", false)]
    public void IsSupportedImage_RecognizesOnlyImageExtensions(string fileName, bool expected)
    {
        Assert.Equal(expected, ImageTextService.IsSupportedImage(fileName));
    }
}
