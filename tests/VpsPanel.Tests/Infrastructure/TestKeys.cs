using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VpsPanel.Tests.Infrastructure;

internal sealed record TestKeyPair(string PrivateKeyPem, string AuthorizedKey);

internal static class TestKeys
{
    public static TestKeyPair Create()
    {
        using var rsa = RSA.Create(2048);
        return new TestKeyPair(rsa.ExportRSAPrivateKeyPem(), ToAuthorizedKey(rsa));
    }

    private static string ToAuthorizedKey(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        using var stream = new MemoryStream();
        WriteBlock(stream, "ssh-rsa"u8);
        WriteMpint(stream, parameters.Exponent!);
        WriteMpint(stream, parameters.Modulus!);
        return "ssh-rsa " + Convert.ToBase64String(stream.ToArray()) + " vpspanel-tests";
    }

    private static void WriteMpint(Stream stream, byte[] value)
    {
        if (value[0] >= 0x80)
            WriteBlock(stream, [0, .. value]);
        else
            WriteBlock(stream, value);
    }

    private static void WriteBlock(Stream stream, ReadOnlySpan<byte> block)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, block.Length);
        stream.Write(length);
        stream.Write(block);
    }
}