using System.Text;

namespace PolarAd.Core.Dns;

/// <summary>
/// Minimal DNS wire-format helpers. We never need to fully decode/encode records:
/// queries are forwarded as raw bytes to the upstream resolver, and for blocked
/// domains we synthesize a small NXDOMAIN response that echoes the question section.
/// </summary>
public static class DnsMessageUtils
{
    private const int HeaderSize = 12;

    /// <summary>
    /// Extracts the QNAME of the first question in a DNS query packet, e.g. "ads.example.com".
    /// Returns null if the packet is too short or malformed.
    /// </summary>
    public static string? TryReadQuestionName(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderSize + 5)
        {
            return null;
        }

        int qdCount = (packet[4] << 8) | packet[5];
        if (qdCount < 1)
        {
            return null;
        }

        var sb = new StringBuilder();
        int pos = HeaderSize;

        while (pos < packet.Length)
        {
            int len = packet[pos];
            if (len == 0)
            {
                pos++;
                break;
            }

            // Compression pointer should not appear in the first question of a well-formed
            // query; bail out rather than risk an infinite loop on malformed input.
            if ((len & 0xC0) == 0xC0)
            {
                return null;
            }

            pos++;
            if (pos + len > packet.Length)
            {
                return null;
            }

            if (sb.Length > 0)
            {
                sb.Append('.');
            }
            sb.Append(Encoding.ASCII.GetString(packet.Slice(pos, len)));
            pos += len;
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// Builds a minimal NXDOMAIN response that echoes the original query's ID and question.
    /// </summary>
    public static byte[] BuildNxDomainResponse(ReadOnlySpan<byte> query)
    {
        // Find end of question section (name + qtype(2) + qclass(2)).
        int pos = HeaderSize;
        while (pos < query.Length && query[pos] != 0)
        {
            int len = query[pos];
            if ((len & 0xC0) == 0xC0)
            {
                pos += 2;
                break;
            }
            pos += 1 + len;
        }
        if (pos < query.Length && query[pos] == 0)
        {
            pos++;
        }
        pos += 4; // qtype + qclass

        int questionEnd = Math.Min(pos, query.Length);
        var response = new byte[questionEnd];
        query[..questionEnd].CopyTo(response);

        // ID stays the same (bytes 0-1).
        response[2] = 0x81; // QR=1, Opcode=0, AA=0, TC=0, RD=1
        response[3] = 0x83; // RA=1, Z=0, RCODE=3 (NXDOMAIN)
        // QDCOUNT stays 1 (bytes 4-5 unchanged from original).
        response[6] = 0; response[7] = 0;  // ANCOUNT = 0
        response[8] = 0; response[9] = 0;  // NSCOUNT = 0
        response[10] = 0; response[11] = 0; // ARCOUNT = 0

        return response;
    }
}
