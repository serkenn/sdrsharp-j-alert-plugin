// Inflate + parse an assembled file into a DecodedAlert, plus a tiny
// non-validating XML tag scanner for lifting headline fields. Ported from
// src/decode/alert.cpp.

using System.Text;
using SDRSharp.JAlert.Frame;

namespace SDRSharp.JAlert.Decode
{
    internal static class AlertDecoder
    {
        // Locate the gzip member inside an assembled JMA file. The body is a JMA
        // Socket Packet:
        //   len[8 ASCII] type[2]("BI", case-insensitive) BCH(bch_len) AN(an_len) gzip…
        //   bch_len = (data[10] & 0x0f) * 4,  an_len = data[19],  gzip @ 10+bch_len+an_len
        // Falls back to scanning for the gzip magic if the header doesn't fit.
        private static int FindGzipOffset(byte[] d)
        {
            if (d.Length >= 20 &&
                (d[8] == 'B' || d[8] == 'b') && (d[9] == 'I' || d[9] == 'i'))
            {
                int bchLen = (d[10] & 0x0f) * 4;
                int anLen = d[19];
                int off = 10 + bchLen + anLen;
                if (off + 3 <= d.Length && d[off] == 0x1f && d[off + 1] == 0x8b && d[off + 2] == 0x08)
                    return off;
            }
            for (int i = 0; i + 3 <= d.Length; ++i)
            {
                if (d[i] == 0x1f && d[i + 1] == 0x8b && d[i + 2] == 0x08)
                    return i;
            }
            return -1;
        }

        private static bool IsSpace(char c) => c == ' ' || c == '\n' || c == '\r' || c == '\t';

        private static string Trim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsSpace(s[a])) ++a;
            while (b > a && IsSpace(s[b - 1])) --b;
            return s.Substring(a, b - a);
        }

        // Extract the text of the next <tag>…</tag> occurrence at/after `from`,
        // advancing `from` past it (or to string end). Mirrors the C++
        // find_tag_from.
        private static string FindTagFrom(string xml, string tag, ref int from)
        {
            if (from < 0 || from > xml.Length) { from = -1; return ""; }
            string open = "<" + tag;
            string close = "</" + tag + ">";
            int p = xml.IndexOf(open, from, System.StringComparison.Ordinal);
            while (p >= 0)
            {
                int after = p + open.Length;
                if (after < xml.Length &&
                    (xml[after] == '>' || xml[after] == ' ' || xml[after] == '/' ||
                     xml[after] == '\t' || xml[after] == '\n' || xml[after] == '\r'))
                {
                    int gt = xml.IndexOf('>', after);
                    if (gt < 0) break;
                    if (xml[gt - 1] == '/') { from = gt + 1; return ""; }
                    int c = xml.IndexOf(close, gt + 1, System.StringComparison.Ordinal);
                    if (c < 0) break;
                    from = c + close.Length;
                    return xml.Substring(gt + 1, c - (gt + 1));
                }
                p = xml.IndexOf(open, after, System.StringComparison.Ordinal);
            }
            from = -1;
            return "";
        }

        // Extract the text of the first <tag>…</tag> occurrence (namespace-
        // insensitive, non-validating). Exposed for testing.
        public static string XmlFirstTag(string xml, string tag)
        {
            int from = 0;
            return Trim(FindTagFrom(xml, tag, ref from));
        }

        // Inflate + parse an assembled file into a DecodedAlert. Always copies the
        // metadata; sets Ok + Xml + field extracts only when the gzip member
        // inflates.
        public static DecodedAlert Decode(AssembledFile f)
        {
            DecodedAlert a = new DecodedAlert
            {
                ChunkType = f.Type,
                Timestamp = f.Timestamp,
                Id = f.Id,
                Filename = f.Filename,
                Flags = f.Flags,
            };

            a.GzipOffset = FindGzipOffset(f.Data);
            if (a.GzipOffset >= 0 &&
                Gunzip.TryInflate(f.Data, a.GzipOffset, f.Data.Length - a.GzipOffset, out byte[] xml))
            {
                a.Ok = true;
                a.Xml = xml;
            }
            else
            {
                // Not a gzipped JMA telegram — keep the raw recovered file so the
                // payload is still reported (e.g. via JSONL data_b64).
                a.Data = f.Data;
                return a;
            }

            string s = Encoding.UTF8.GetString(a.Xml);
            int from = 0;
            a.ControlTitle = Trim(FindTagFrom(s, "Title", ref from));  // first = Control
            a.HeadTitle = Trim(FindTagFrom(s, "Title", ref from));     // second = Head
            a.InfoType = XmlFirstTag(s, "InfoType");
            a.ReportDateTime = XmlFirstTag(s, "ReportDateTime");
            a.HeadlineText = XmlFirstTag(s, "Text");
            return a;
        }
    }
}
