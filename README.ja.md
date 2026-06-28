# sdrsharp-j-alert-plugin

*English: [README.md](README.md)*

[SDR# (SDRSharp)](https://airspy.com/download/) 用のプラグインで、**J-ALERT**
（全国瞬時警報システム）の衛星ダウンリンクを復調・デコードします。地上系を一切
参照せず、完全オフラインで RF から地上系と同一の NowcastPacket を復元します。
gzip 圧縮された JMA 電文チャンクは、さらに展開して警報 XML まで復元します。

本プラグインは、元々 SDR++ 向けに書かれた信号処理コアの C# / .NET 移植版です。
DSP・FEC・フレーミング・デコードの各段はバイト単位で等価であり、プラットフォーム
層のみを SDR# のプラグイン API（`ISharpPlugin` + `IIQProcessor` + WinForms サイド
パネル）に合わせて書き直しています。

J-ALERT 衛星リンクは、暗号化されていない一般的な COTS 衛星モデム波形です。

| 項目 | 値 |
|---|---|
| 変調 | BPSK、連続 SCPC キャリア |
| シンボルレート | 256 ksym/s（FEC 後 128 kbps） |
| FEC | 畳み込み符号 K=7、生成多項式 (171, 133)₈（"Voyager"）、レート 1/2 |
| スクランブラ | IESS-308 自己同期型、1 + x³ + x²⁰ |
| フレーミング | HDLC（0x7E フラグ、ビットスタッフィング）、CRC-16/X.25 FCS |
| ペイロード | NowcastPacket（チャンク化）。JMA 電文チャンクは gzip メンバ → 警報 XML を内包 |
| 暗号化 | なし |

## 信号処理チェーン

```
SDR# DecimatedAndFilteredIQ（スペクトラム中心周波数を中心とする）
 └─ NCO シフト（Frequency − CenterFrequency → ベースバンド）
 └─ 粗キャリア回復（二乗信号のブロック FFT ピーク検出 → 2·fc）
 └─ 1.024 MHz へポリフェーズ・リサンプリング（4 sps × 256 ksym/s）
 └─ AGC
 └─ 整合フィルタ + シンボルタイミング（ポリフェーズ RRC β=0.35 + Müller-Müller TED）
 └─ 判定指向 Costas ループ → ソフトシンボル
 └─ 軟判定ビタビ（K=7, (171,133)、ストリーミング・トレースバック）
 └─ 二重差動復号 + 反転（BPSK 180° 位相曖昧性を解決）
 └─ IESS-308 自己同期デスクランブル（s ⊕ s≪3 ⊕ s≪20）
 └─ HDLC デフレーム + デスタッフ（0x7E フラグ、CRC-16/X.25 検証）
 └─ NowcastPacket 解析（ヘッダ + チャンクエントリ、チャンクごとに body CRC-32）
 └─ チャンク再構成 → 地上系とバイト単位で一致する復元ファイル
                     （id + seq による複数チャンク／複数パケット対応）
 └─ ［任意］gzip 圧縮 JMA 電文チャンク（flags==3, wrmx/eprx/issx/ioeq）の場合:
        JMA ソケットパケットヘッダ → gunzip → JMA 警報 XML
```

バイト単位で正確な復元成果物は NowcastPacket／再構成チャンクファイルです。XML への
展開は gzip 圧縮 JMA 電文チャンク（`flags==3`）にのみ適用される任意の最終段です。
gzip でない／JMA でないチャンクも復元・報告されますが、XML 化はされません。

レート 1/2 符号のペアリング位相だけが唯一残る曖昧性です。受信機は位相ごとに 2 本の
ビタビ + フレーム処理チェーンを並列実行し、HDLC FCS に正しい方を選ばせます（誤った
位相は CRC を通らないゴミしか生成しません）。キャリア位相／ビット極性の曖昧性は
差動復号が吸収します。パケットの `header_crc32` は意図的に無視します（プロトコル
変換器の既知のバグで常に不正）。チャンクごとの `body_crc32` は検証します。

## リポジトリ構成

```
.
├── SDRSharp.JAlert/
│   ├── SDRSharp.JAlert.csproj   # net8.0-windows, AnyCPU, WinForms, unsafe
│   ├── JAlertPlugin.cs          # ISharpPlugin エントリポイント
│   ├── JAlertProcessor.cs       # IIQProcessor ストリームフック（NCO + Receiver + シンク）
│   ├── JAlertPanel.cs           # WinForms サイドパネル
│   ├── JAlertSettings.cs        # JSON 設定（%APPDATA%\SDRSharp.JAlert）
│   ├── Dsp/                     # Complex32, Fft, Agc, FilterDesign, リサンプラ, PFB クロック同期, BpskDemod
│   ├── Fec/                     # 畳み込み符号 + ストリーミング軟判定ビタビ
│   ├── Frame/                   # 差動, デスクランブラ, HDLC, CRC-32, NowcastPacket, 再構成
│   ├── Decode/                  # gunzip, 警報モデル + デコーダ, ビットパイプライン, 受信機
│   ├── Sink/                    # ファイル / TCP JSONL シンク + 警報 JSON シリアライザ
│   └── Ui/                      # GDI+ スパークライン + コンステレーション表示
├── sdk/                         # SDR# 参照（スタブ）アセンブリ一式
├── .github/workflows/build.yml  # CI: ビルド + リリース
└── docs/JSONL_FORMAT.md         # JSONL 出力スキーマ
```

## ビルド

**.NET 8（`net8.0-windows`）、AnyCPU** を対象とします。現行の SDR# パッケージは
`SDRSharp.dotnet8.exe` と `SDRSharp.dotnet9.exe` の両方を同梱し、既定は .NET 8
ホストです（changelog に "reverted to dotnet 8.0 until the plugins follow"）。
`net8.0` プラグインは**両方**のホストで読み込めますが、`net9.0` プラグインは
.NET 8 ホストでは読み込めません。

### GitHub Actions（推奨）

`.github/workflows/build.yml` は push のたびにプラグインをビルドし、`v*` タグを
push したとき（例: `git tag v1.0.0 && git push --tags`）にリリースへ成果物を
添付します。

現行 SDR# は単一ファイルの自己完結型 exe のため、管理対象の `SDRSharp.*`
アセンブリは個別 DLL として配布されません。プラグインは [`sdk/`](sdk/) 配下の
最小の**参照（スタブ）アセンブリ**に対してコンパイルし、実行時に SDR# 本体の
実アセンブリへバインドします。詳細は [sdk/README.md](sdk/README.md)。

### ローカルビルド

```sh
# 参照スタブを ./libs にビルドしてから本体をビルド
dotnet build sdk/SDRSharp.Radio/SDRSharp.Radio.csproj   -c Release
dotnet build sdk/SDRSharp.PanView/SDRSharp.PanView.csproj -c Release
dotnet build sdk/SDRSharp.Common/SDRSharp.Common.csproj  -c Release
mkdir -p libs && cp sdk/*/bin/Release/net8.0-windows/SDRSharp.*.dll libs/

dotnet build SDRSharp.JAlert/SDRSharp.JAlert.csproj -c Release
```

（Windows 以外のホストでは各コマンドに `/p:EnableWindowsTargeting=true` を追加。
コンパイルは通りますが、実行は Windows 上の SDR# 内に限られます。）

## SDR# へのインストール

現行 SDR# は `Plugins/` ディレクトリ（`SDRSharp.config` の
`core.pluginsDirectory`）からプラグインを**自動検出**します。`Plugins.json` も
設定ファイルの編集も**不要**です。

1. SDR# の `Plugins` ディレクトリ内にフォルダを作り、DLL を入れます。例:
   `Plugins/SDRSharp.JAlert/SDRSharp.JAlert.dll`（リリースの zip はこの構成済み
   なので、`Plugins/` に展開するだけで OK）。
2. SDR#（既定の `SDRSharp.dotnet8.exe`）を起動。**Plugins** メニューに
   **J-ALERT Decoder** が表示されます。
3. **Play** して J-ALERT キャリアに同調します。デシメーション／ズームを、
   キャリア周辺に少なくとも約 400 kHz の IQ 帯域が残るように設定してください
   （信号は約 346 kHz を占有）。

プラグインが表示されない場合は、SDR# フォルダの `PluginError.log` に読み込み
エラーが記録されます。

パネルにはロック状態、BPSK コンステレーション、キャリア／Costas オフセット、推定
ビット誤り率、HDLC フレーム数、および最新のデコード済み警報（タイトル／見出し／
時刻）が表示されます。

## 出力

復元ファイルは次の方法で出力できます。

- **ファイル**および／または **TCP** ポートへ JSONL としてストリーミング。各復元
  ファイルをペイロードをインラインで含めて出力します（gzip 圧縮 JMA 電文は警報
  XML、XML でない電文は生バイトの base64）。詳細は
  [docs/JSONL_FORMAT.md](docs/JSONL_FORMAT.md)
  （[English](docs/JSONL_FORMAT.md)）を参照、
- **出力フォルダ**への書き出し（**File output** チェックボックスで有効化）:
  デコード済み JMA 電文は `<timestamp>.xml`、その他（XML でない）電文は生バイトを
  `<timestamp>.<chunk_type>.bin` として保存。

出力パスの既定値は `%APPDATA%\SDRSharp.JAlert\j_alert\`（JSONL ファイルは同所の
`decoded.jsonl`）です。すべての出力設定は
`%APPDATA%\SDRSharp.JAlert\settings.json` に保存されます。

## 対応プラットフォーム

| 対象 | 対応 |
|---|---|
| Windows（SDR#、.NET 8） | 対応 |
| Linux / macOS | 非対応（SDR# は Windows 専用） |

## ライセンス

MIT License — [LICENSE](LICENSE) を参照。Copyright (c) 2026 KIRISHIKI Yudai.
