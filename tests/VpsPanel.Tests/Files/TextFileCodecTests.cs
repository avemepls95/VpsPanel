using System.Text;
using VpsPanel.Files;

namespace VpsPanel.Tests.Files;

public sealed class TextFileCodecTests
{
    [Fact(DisplayName = "UTF-8 with BOM and CRLF => text without BOM, BOM flag, CRLF separator")]
    public void Inspect_Should_DetectBomAndCrlf_When_Present()
    {
        // Arrange
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "line1\r\nline2"u8];

        // Act
        var inspection = TextFileCodec.Inspect(bytes);

        // Assert
        inspection.Should().Be(new TextInspection(true, null, "line1\r\nline2", true, LineSeparator.Crlf));
    }

    [Fact(DisplayName = "UTF-8 without BOM with LF => LF separator")]
    public void Inspect_Should_DetectLf_When_NoCrlf()
    {
        // Arrange
        var bytes = "Héllo\nwörld"u8.ToArray();

        // Act
        var inspection = TextFileCodec.Inspect(bytes);

        // Assert
        inspection.Should().Be(new TextInspection(true, null, "Héllo\nwörld", false, LineSeparator.Lf));
    }

    [Fact(DisplayName = "Null byte at the start of the file => binary file")]
    public void Inspect_Should_ReportBinary_When_NullByteFound()
    {
        // Arrange
        byte[] bytes = [0x61, 0x00, 0x62];

        // Act
        var inspection = TextFileCodec.Inspect(bytes);

        // Assert
        inspection.Should().Be(new TextInspection(false, FileNotEditableReason.Binary, null, false, LineSeparator.Lf));
    }

    [Fact(DisplayName = "Invalid UTF-8 sequence => not UTF-8")]
    public void Inspect_Should_ReportNotUtf8_When_BytesInvalid()
    {
        // Arrange
        byte[] bytes = [0xC0, 0x20, 0x41];

        // Act
        var inspection = TextFileCodec.Inspect(bytes);

        // Assert
        inspection.Should().Be(new TextInspection(false, FileNotEditableReason.NotUtf8, null, false, LineSeparator.Lf));
    }

    [Fact(DisplayName = "Text with the BOM flag => bytes with BOM at the start")]
    public void Encode_Should_PrependBom_When_Requested()
    {
        // Arrange
        const string text = "a\r\nb";

        byte[] expected = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\r\nb")];

        // Act
        var bytes = TextFileCodec.Encode(text, hasBom: true);

        // Assert
        bytes.Should().Equal(expected);
    }

    [Fact(DisplayName = "Bytes => lowercase SHA-256")]
    public void Sha256_Should_ReturnLowerHex_When_Called()
    {
        // Arrange
        var bytes = "abc"u8.ToArray();

        // Act
        var hash = TextFileCodec.Sha256(bytes);

        // Assert
        hash.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }
}