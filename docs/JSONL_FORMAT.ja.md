# JSONL 出力フォーマット

*English: [JSONL_FORMAT.md](JSONL_FORMAT.md)*

J-ALERT デコーダプラグインは、復号したアラートを
[JSON Lines](https://jsonlines.org/)（JSONL）形式で 2 つのシンクへ出力できます。

- **ファイル** — 設定したパスへ追記。1 行ごとにフラッシュされます。
- **TCP** — 設定したポートに接続している全クライアントへブロードキャストします。

両シンクは**同一の行内容**を出力します。各シンクはレコードごとに末尾へ `\n` を
1 つ付加します。レコード自体に改行は含まれません（ペイロード中の `\n` は
エスケープされます。[エンコーディング](#エンコーディング)参照）。

シリアライザは [`src/sink/alert_json.cpp`](../src/sink/alert_json.cpp)
（`serialize_alert`）です。

## 行が出力される契機

**再結合されたファイル 1 件につき 1 行**が出力されます。すなわち、NowcastPacket
のチャンク（複数チャンクファイルの場合は最終チャンク）がファイルを完成させる
たびに 1 行です。具体的には：

- 1 つ以上のチャンクを含む NowcastPacket → 完成したファイルごとに 1 行。
- 複数チャンク／複数パケットにまたがるファイル → 最後のチャンクが到着して
  再結合された時点で **1 行**。
- **ステータス／キープアライブ**パケット（チャンクを持たない空の NowcastPacket）
  → **行を出力しません**。ステータスパケットはパネル上で計数されるのみです。

gzip 本体の展開に失敗した場合でも行は出力されます（`"decoded": false`）。
メタデータ系フィールドは残るため、チャンクが到着したことは利用側で判別できます。
完全な XML（`xml`）は成功時のみ含まれます。

## レコードのスキーマ

各行は単一の JSON オブジェクトです。フィールド一覧：

| フィールド    | 型      | 常時? | 説明 |
|---------------|---------|-------|------|
| `rx_time_ms`  | number  | ○     | 復号時点の受信機ウォールクロック。Unix エポックからのミリ秒（UTC）。 |
| `decoded`     | boolean | ○     | チャンクが gzip 済み JMA 電文で、本体を XML に展開できたら `true`。それ以外（非 XML 電文）は `false`。**どのペイロードフィールドが出るかを決定する**（後述）。 |
| `flags`       | number  | ○     | チャンクメタデータの flags。`3` = 搬送ファイルが gzip 圧縮。 |
| `chunk_type`  | string  | ○     | 4 文字のチャンク種別コード（小文字）。コンテンツ分類を表す。JMA 種別: `wrmx`（気象警報・注意報）, `eprx`, `issx`, `ioeq`。 |
| `packet_time` | string  | ○\*   | NowcastPacket のタイムスタンプ。17 桁 ASCII `YYYYMMDDhhmmssSSS`（JST）。不正な場合は空文字列。 |
| `id`          | string  | —     | 同一ファイルの全チャンクで共有される 4 文字のファイル ID。空なら省略。 |
| `filename`    | string  | —     | チャンクメタデータのファイル名（例 `00000000.xml.gz`）。無ければ省略。 |
| `title`       | string  | —     | JMA `<Control><Title>`（例 `気象特別警報・警報・注意報`）。復号時のみ／見つからなければ省略。 |
| `head_title`  | string  | —     | JMA `<Head><Title>`（例 `東京都気象警報・注意報`）。復号時のみ／見つからなければ省略。 |
| `info_type`   | string  | —     | JMA `<Head><InfoType>` — `発表` / `更新` / `取消`。復号時のみ／見つからなければ省略。 |
| `report_time` | string  | —     | JMA `<Head><ReportDateTime>`。JST オフセット付き ISO-8601。復号時のみ／見つからなければ省略。 |
| `headline`    | string  | —     | JMA `<Head><Headline><Text>` — 人間可読の要約文。復号時のみ／見つからなければ省略。 |
| `xml_bytes`   | number  | 復号時 | 展開後 XML のバイト数。**`decoded` が `true` のときのみ**存在。 |
| `xml`         | string  | 復号時 | 展開済み JMA アラート XML の**全文**。`decoded` が `true`（かつ XML が非空）のとき存在。 |
| `data_bytes`  | number  | 非XML時 | 復元した生ファイルのバイト数。**`decoded` が `false` のときのみ**存在。 |
| `data_b64`    | string  | 非XML時 | 復元した生ファイルの Base64（RFC 4648）。`decoded` が `false`（かつ非空）のとき存在。 |

\* `packet_time` と `chunk_type` は常にシリアライズされますが、`packet_time` は
タイムスタンプ不正時に空文字列になることがあります。

`title` / `head_title` / `info_type` / `report_time` / `headline` は、軽量かつ
非検証のタグ走査で XML から抽出した値です。あくまでプレビュー用途とし、
権威的な内容は `xml` をパースして取得してください。

> **ペイロードフィールドは `decoded` で分岐します。** 1 行には、`decoded` が
> `true` のとき XML 系（`xml_bytes`＋任意の `xml`）、`false` のとき生データ系
> （`data_bytes`＋任意の `data_b64`）の**どちらか一方のみ**が含まれます
> （両方は出ません）。まず `decoded` で分岐してください。

> **フィールド順序・前方互換性。** フィールドは上表の順で出力されますが、
> 利用側は名前でキー参照し、未知／将来のフィールドを許容してください。
> 任意フィールドは空のとき `null` ではなく**省略**されます。

## ペイロード

各行はペイロードを常にインラインで持ちます（復号 JMA 電文は `xml`、非 XML 電文は
`data_b64`。いずれも数十 KB に及ぶことがあります）。サイズ欄
（`xml_bytes` / `data_bytes`）も常に併記されます。

同じファイルは **Output folder** オプションでディスクにも書き出されます
（復号電文は `<packet_time>.xml`、非 XML 電文は
`<packet_time>.<chunk_type>.bin`）。

## 例

気象警報・注意報の例（大きい `xml` 欄は下に別掲。可読性のため整形、実際の出力は 1 行）：

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

同じ行には全文 XML も含まれます：

```json
{ …, "xml": "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Report …>…</Report>\n" }
```

非 XML 電文（非 gzip／非 JMA チャンク）は代わりに `data_bytes` と `data_b64`
を持ちます：

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

## エンコーディング

- 各レコードは UTF-8 です。マルチバイト UTF-8（日本語テキスト）は
  **そのまま**通過します。ASCII への `\u` エスケープは行いません。
- JSON 文字列内では `"`、`\`、および C0 制御文字のみをエスケープします：
  `\"`、`\\`、`\n`、`\r`、`\t`、および `0x20` 未満のその他制御文字は `\u00XX`。
- したがって同梱される `xml` 文字列は元の文書バイトを保持します
  （その `\n`/`\r` は `\n`/`\r` になります）。JSON 文字列をデコードすると、
  展開済み XML がそのまま得られます。

## 利用方法

ファイルシンクを追尾：

```sh
tail -F alerts.jsonl | jq -c '{packet_time, chunk_type, head_title, info_type}'
```

TCP シンクへ接続（既定ポート 7355）：

```sh
nc 127.0.0.1 7355 | jq -r 'select(.decoded) | "\(.packet_time) \(.head_title)"'
```

Python — 要約を表示し、さらに各全文 XML を packet_time をキーにファイルへ
書き出す（後者は *Include XML in JSONL* が ON であること）：

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

（プラグインの **XML output folder** オプションは、JSONL に `xml` を含めなくても
同じファイル書き出しを直接行います。）
