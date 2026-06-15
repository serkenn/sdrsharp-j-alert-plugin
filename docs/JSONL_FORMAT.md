# JSONL output format

*日本語: [JSONL_FORMAT.ja.md](JSONL_FORMAT.ja.md)*

The J-ALERT decoder plugin can stream decoded alerts as
[JSON Lines](https://jsonlines.org/) (JSONL) to two sinks:

- **File** — appended to the configured path, flushed after every line.
- **TCP** — broadcast to every client connected to the configured port.

Both sinks emit the **same line content**. Each sink appends a single `\n`
after each record; the record itself contains no embedded newline (any `\n` in
the payload is escaped — see [Encoding](#encoding)).

The serializer is [`src/sink/alert_json.cpp`](../src/sink/alert_json.cpp)
(`serialize_alert`).

## When a line is emitted

One line is emitted **per reassembled file** — i.e. each time a NowcastPacket
chunk (or the final chunk of a multi-chunk file) completes a file. Concretely:

- A NowcastPacket carrying one or more chunks → one line per completed file.
- A multi-chunk / multi-packet file → **one** line, emitted when the last chunk
  arrives and the file is reassembled.
- A **status / keep-alive** packet (an empty NowcastPacket with no chunks)
  produces **no line**. Status packets are only counted in the panel.

A line is emitted even when the gzip body fails to inflate
(`"decoded": false`); the metadata fields are still present so a consumer can
see that a chunk arrived. The full XML (`xml`) is only present on success.

## Record schema

Each line is a single JSON object. Fields:

| Field         | Type    | Always? | Description |
|---------------|---------|---------|-------------|
| `rx_time_ms`  | number  | yes     | Receiver wall-clock at decode time, milliseconds since the Unix epoch (UTC). |
| `decoded`     | boolean | yes     | `true` if the chunk was a gzipped JMA telegram and its body inflated to XML; `false` for any other (non-XML) telegram. **Selects which payload fields appear** (see below). |
| `flags`       | number  | yes     | Chunk metadata flags. `3` = the carried file is gzipped. |
| `chunk_type`  | string  | yes     | 4-char chunk-type code, lower-cased — the content category. JMA types: `wrmx` (weather warnings), `eprx`, `issx`, `ioeq`. |
| `packet_time` | string  | yes\*   | NowcastPacket timestamp, 17 ASCII digits `YYYYMMDDhhmmssSSS` (JST). Empty string if the field was malformed. |
| `id`          | string  | no      | 4-char file id shared by all chunks of one file. Omitted if empty. |
| `filename`    | string  | no      | File name from the chunk metadata, e.g. `00000000.xml.gz`. Omitted if absent. |
| `title`       | string  | no      | JMA `<Control><Title>`, e.g. `気象特別警報・警報・注意報`. Decoded telegrams only; omitted if not found. |
| `head_title`  | string  | no      | JMA `<Head><Title>`, e.g. `東京都気象警報・注意報`. Decoded only; omitted if not found. |
| `info_type`   | string  | no      | JMA `<Head><InfoType>` — `発表` / `更新` / `取消`. Decoded only; omitted if not found. |
| `report_time` | string  | no      | JMA `<Head><ReportDateTime>`, ISO-8601 with JST offset. Decoded only; omitted if not found. |
| `headline`    | string  | no      | JMA `<Head><Headline><Text>` — the human-readable summary. Decoded only; omitted if not found. |
| `xml_bytes`   | number  | decoded | Size of the inflated XML in bytes. Present **only when `decoded` is `true`**. |
| `xml`         | string  | decoded | The **full** inflated JMA alert XML. Present when `decoded` is `true` (and the XML is non-empty). |
| `data_bytes`  | number  | non-XML | Size of the raw recovered file in bytes. Present **only when `decoded` is `false`**. |
| `data_b64`    | string  | non-XML | Base64 (RFC 4648) of the raw recovered file. Present when `decoded` is `false` (and the file is non-empty). |

\* `packet_time` and `chunk_type` are always serialized, but `packet_time` may be
an empty string for a malformed timestamp.

The `title` / `head_title` / `info_type` / `report_time` / `headline` fields are
lifted from the XML by a lightweight, non-validating tag scan; treat them as a
convenience preview and parse `xml` for authoritative content.

> **Payload fields are keyed on `decoded`.** A line carries **either** the XML
> pair (`xml_bytes`, optional `xml`) when `decoded` is `true`, **or** the raw
> pair (`data_bytes`, optional `data_b64`) when `decoded` is `false` — never
> both. Branch on `decoded` first.

> **Field order / forward-compatibility.** Fields are emitted in the order
> above, but consumers should key by name and tolerate unknown / future fields.
> Optional fields are *omitted* (not `null`) when empty.

## Payload

Every line carries its full payload inline — `xml` for decoded JMA telegrams,
`data_b64` for non-XML ones (often tens of kilobytes each). The size field
(`xml_bytes` / `data_bytes`) always accompanies it.

The same files are also written to disk by the **Output folder** option:
`<packet_time>.xml` for decoded telegrams and `<packet_time>.<chunk_type>.bin`
for non-XML ones.

## Example

A decoded weather-warning alert (the large `xml` field is shown separately
below; pretty-printed here for readability — actual output is one line):

```json
{
  "rx_time_ms": 1781570930242,
  "decoded": true,
  "flags": 3,
  "chunk_type": "wrmx",
  "packet_time": "20260615003850242",
  "id": "5390",
  "filename": "00000000.xml.gz",
  "title": "気象特別警報・警報・注意報",
  "head_title": "東京都気象警報・注意報",
  "info_type": "発表",
  "report_time": "2026-06-15T00:38:00+09:00",
  "headline": "伊豆諸島南部では、土砂災害や落雷に注意してください。…",
  "xml_bytes": 32122
}
```

The same line also carries the full XML:

```json
{ …, "xml": "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Report …>…</Report>\n" }
```

A non-XML telegram (a non-gzipped / non-JMA chunk) instead carries `data_bytes`
and `data_b64`:

```json
{
  "rx_time_ms": 1781570930242,
  "decoded": false,
  "flags": 0,
  "chunk_type": "abcd",
  "packet_time": "20260615003850242",
  "id": "Z9",
  "filename": "payload.bin",
  "data_bytes": 1024,
  "data_b64": "AAEC//4g…"
}
```

## Encoding

- Each record is UTF-8. Multibyte UTF-8 (Japanese text) is passed through
  **verbatim** — fields are not `\u`-escaped to ASCII.
- Within JSON strings, only `"`, `\`, and the C0 control characters are escaped:
  `\"`, `\\`, `\n`, `\r`, `\t`, and `\u00XX` for other controls below `0x20`.
- The embedded `xml` string therefore preserves the original document bytes
  (its `\n`/`\r` become `\n`/`\r`); decoding the JSON string yields the exact
  inflated XML.

## Consuming

Tail a file sink:

```sh
tail -F alerts.jsonl | jq -c '{packet_time, chunk_type, head_title, info_type}'
```

Connect to a TCP sink (port 7355 by default):

```sh
nc 127.0.0.1 7355 | jq -r 'select(.decoded) | "\(.packet_time) \(.head_title)"'
```

Python — print a summary, and extract every full XML document to a file keyed by
packet time (the latter requires *Include XML in JSONL* on):

```python
import json
with open("alerts.jsonl", encoding="utf-8") as f:
    for line in f:
        rec = json.loads(line)
        if not rec["decoded"]:
            continue
        print(rec["packet_time"], rec["chunk_type"], rec.get("head_title", ""))
        if "xml" in rec:
            with open(rec["packet_time"] + ".xml", "w", encoding="utf-8") as out:
                out.write(rec["xml"])
```

(The plugin's **XML output folder** option does the same file extraction
directly, without needing `xml` in the JSONL.)
