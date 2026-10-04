using System.Text;

namespace PolarAd.Tests;

/// <summary>
/// Builds minimal, valid DNS query packets for tests, without depending on any
/// OS resolver.
/// </summary>
public static class DnsTestHelper
{
    public static byte[] BuildQuery(int id, string domain, ushort qtype = 1)
    {
        using var ms = new MemoryStream();

        void WriteU16(int v)
        {
            ms.WriteByte((byte)(v >> 8));
            ms.WriteByte((byte)v);
        }

        WriteU16(id);
        WriteU16(0x0100); // standard query, recursion desired
        WriteU16(1);      // QDCOUNT = 1
        WriteU16(0);      // ANCOUNT
        WriteU16(0);      // NSCOUNT
        WriteU16(0);      // ARCOUNT

        foreach (var label in domain.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0); // root label

        WriteU16(qtype); // QTYPE (1 = A)
        WriteU16(1);     // QCLASS = IN

        return ms.ToArray();
    }
}
