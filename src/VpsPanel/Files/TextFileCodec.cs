using System.Security.Cryptography;
using System.Text;

namespace VpsPanel.Files;

internal static class TextFileCodec
{
    public const int MaxEditableBytes = 1024 * 1024;
    private const int BinaryProbeBytes = 8192;
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TextInspection Inspect(byte[] bytes)
    {
        if (bytes.AsSpan(0, Math.Min(BinaryProbeBytes, bytes.Length)).Contains((byte)0))
            return NotEditable(FileNotEditableReason.Binary);

        var hasBom = bytes.AsSpan().StartsWith(Bom);
        var offset = hasBom ? Bom.Length : 0;
        try
        {
            var text = Strict.GetString(bytes, offset, bytes.Length - offset);
            return new TextInspection(true, null, text, hasBom, text.Contains("\r\n") ? LineSeparator.Crlf : LineSeparator.Lf);
        }
        catch (DecoderFallbackException)
        {
            return NotEditable(FileNotEditableReason.NotUtf8);
        }
    }

    public static byte[] Encode(string text, bool hasBom) =>
        hasBom ? [.. Bom, .. Strict.GetBytes(text)] : Strict.GetBytes(text);

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static TextInspection NotEditable(FileNotEditableReason reason) => new(false, reason, null, false, LineSeparator.Lf);
}