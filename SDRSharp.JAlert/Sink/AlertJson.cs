// Serialize a DecodedAlert to a single JSONL line for the file / TCP sinks.
// Ported from src/sink/alert_json.cpp.
//
// One JSON object, no trailing newline. Includes packet metadata, the lifted JMA
// headline fields, and the inline payload: the full inflated alert XML for
// gzipped JMA telegrams, or base64 of the raw recovered file (data_b64) for
// non-XML telegrams.

using System;
using System.Text;
using SDRSharp.JAlert.Decode;

namespace SDRSharp.JAlert.Sink
{
    public static class AlertJson
    {
        // JSON string escape. The input is a .NET string (UTF-16); we emit UTF-8
        // text in the surrounding stream. Only structural characters and C0
        // controls are escaped; other code points pass through verbatim.
        private static void AppendEscaped(StringBuilder outSb, string s)
        {
            outSb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': outSb.Append("\\\""); break;
                    case '\\': outSb.Append("\\\\"); break;
                    case '\n': outSb.Append("\\n"); break;
                    case '\r': outSb.Append("\\r"); break;
                    case '\t': outSb.Append("\\t"); break;
                    default:
                        if (c < 0x20) outSb.Append("\\u").Append(((int)c).ToString("x4"));
                        else outSb.Append(c);
                        break;
                }
            }
            outSb.Append('"');
        }

        private static void AppendField(StringBuilder outSb, string key, string val, ref bool first)
        {
            if (!first) outSb.Append(',');
            first = false;
            outSb.Append('"').Append(key).Append("\":");
            AppendEscaped(outSb, val);
        }

        public static string Serialize(DecodedAlert a, long timestampMs)
        {
            StringBuilder outSb = new StringBuilder(a.Xml.Length + a.Data.Length * 2 + 512);
            outSb.Append('{');
            bool first = true;

            outSb.Append("\"rx_time_ms\":").Append(timestampMs);
            first = false;

            outSb.Append(",\"decoded\":").Append(a.Ok ? "true" : "false");
            outSb.Append(",\"flags\":").Append((uint)a.Flags);

            AppendField(outSb, "chunk_type", a.ChunkType, ref first);
            AppendField(outSb, "packet_time", a.Timestamp, ref first);
            if (!string.IsNullOrEmpty(a.Id)) AppendField(outSb, "id", a.Id, ref first);
            if (!string.IsNullOrEmpty(a.Filename)) AppendField(outSb, "filename", a.Filename, ref first);
            if (!string.IsNullOrEmpty(a.ControlTitle)) AppendField(outSb, "title", a.ControlTitle, ref first);
            if (!string.IsNullOrEmpty(a.HeadTitle)) AppendField(outSb, "head_title", a.HeadTitle, ref first);
            if (!string.IsNullOrEmpty(a.InfoType)) AppendField(outSb, "info_type", a.InfoType, ref first);
            if (!string.IsNullOrEmpty(a.ReportDateTime)) AppendField(outSb, "report_time", a.ReportDateTime, ref first);
            if (!string.IsNullOrEmpty(a.HeadlineText)) AppendField(outSb, "headline", a.HeadlineText, ref first);

            if (a.Ok)
            {
                // Gzipped JMA telegram: inflated XML.
                outSb.Append(",\"xml_bytes\":").Append(a.Xml.Length);
                if (a.Xml.Length > 0)
                {
                    outSb.Append(",\"xml\":");
                    AppendEscaped(outSb, Encoding.UTF8.GetString(a.Xml));
                }
            }
            else
            {
                // Non-XML telegram (non-gzipped / non-JMA chunk): expose the raw
                // recovered file so the payload is still retrievable.
                outSb.Append(",\"data_bytes\":").Append(a.Data.Length);
                if (a.Data.Length > 0)
                {
                    outSb.Append(",\"data_b64\":\"");
                    outSb.Append(Convert.ToBase64String(a.Data));
                    outSb.Append('"');
                }
            }
            outSb.Append('}');
            return outSb.ToString();
        }
    }
}
