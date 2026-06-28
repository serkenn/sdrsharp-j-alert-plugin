# sdrsharp-j-alert-plugin

*日本語: [README.ja.md](README.ja.md)*

[SDR# (SDRSharp)](https://airspy.com/download/) plugin that demodulates and
decodes the **J-ALERT** (全国瞬時警報システム) satellite downlink — recovering the
ground-system-identical NowcastPacket straight from RF, fully offline and with no
reference to the terrestrial system. Gzipped JMA telegram chunks are additionally
inflated to their alert XML.

This is a C# / .NET port of the signal-processing core originally written for
SDR++; the DSP, FEC, framing and decode stages are byte-for-byte equivalent, with
the platform layer rewritten against SDR#'s plugin API (`ISharpPlugin` +
`IIQProcessor` + a WinForms side panel).

The J-ALERT satellite link is an unencrypted, standard COTS satellite-modem
waveform:

| Item | Value |
|---|---|
| Modulation | BPSK, continuous SCPC carrier |
| Symbol rate | 256 ksym/s (128 kbps after FEC) |
| FEC | Convolutional K=7, generators (171, 133)₈ ("Voyager"), rate 1/2 |
| Scrambler | IESS-308 self-synchronising, 1 + x³ + x²⁰ |
| Framing | HDLC (0x7E flags, bit-stuffing), CRC-16/X.25 FCS |
| Payload | NowcastPacket (chunked); JMA telegram chunks carry a gzip member → alert XML |
| Encryption | none |

## Signal chain

```
SDR# DecimatedAndFilteredIQ  (centered on the spectrum center frequency)
 └─ NCO shift  (Frequency − CenterFrequency → baseband)
 └─ coarse carrier recovery  (block FFT-argmax on the squared signal → 2·fc)
 └─ polyphase resample to 1.024 MHz (4 sps × 256 ksym/s)
 └─ AGC
 └─ matched filter + symbol timing  (polyphase RRC β=0.35 + Müller-Müller TED)
 └─ decision-directed Costas loop  → soft symbols
 └─ soft-decision Viterbi  (K=7, (171,133), streaming traceback)
 └─ double-differential decode + invert      (resolves BPSK 180° ambiguity)
 └─ IESS-308 self-sync descramble  (s ⊕ s≪3 ⊕ s≪20)
 └─ HDLC deframe + destuff  (0x7E flags, CRC-16/X.25 validated)
 └─ NowcastPacket parse  (header + chunk-entries; per-chunk body CRC-32)
 └─ chunk reassembly  → recovered file, byte-exact with the ground system
                        (multi-chunk / multi-packet, by id + seq)
 └─ [optional] gzipped JMA telegram chunks (flags==3, wrmx/eprx/issx/ioeq):
        JMA Socket Packet header → gunzip → JMA alert XML
```

The byte-exact recovered artifact is the NowcastPacket / reassembled chunk file;
inflating it to XML is an optional final step that applies only to gzipped JMA
telegram chunks (`flags==3`). Non-gzipped or non-JMA chunks are still recovered
and reported, just not turned into XML.

The pairing phase of the rate-1/2 code is the only residual ambiguity; the
receiver runs two Viterbi+frame chains in parallel (one per phase) and lets the
HDLC FCS pick the correct one — the wrong phase produces only CRC-failing
garbage. Carrier-phase / bit-polarity ambiguity is absorbed by the differential
decode. The packet `header_crc32` is intentionally ignored (a known protocol-
converter bug makes it always wrong); the per-chunk `body_crc32` is verified.

## Repository layout

```
.
├── SDRSharp.JAlert/
│   ├── SDRSharp.JAlert.csproj   # net8.0-windows, x86, WinForms, unsafe
│   ├── JAlertPlugin.cs          # ISharpPlugin entry point
│   ├── JAlertProcessor.cs       # IIQProcessor stream hook (NCO + Receiver + sinks)
│   ├── JAlertPanel.cs           # WinForms side panel
│   ├── JAlertSettings.cs        # JSON settings (%APPDATA%\SDRSharp.JAlert)
│   ├── Dsp/                     # Complex32, Fft, Agc, FilterDesign, resampler, PFB clock sync, BpskDemod
│   ├── Fec/                     # conv code + streaming soft Viterbi
│   ├── Frame/                   # differential, descrambler, HDLC, CRC-32, NowcastPacket, reassembler
│   ├── Decode/                  # gunzip, alert model + decoder, bit pipeline, receiver
│   ├── Sink/                    # file / TCP JSONL sinks + alert JSON serializer
│   └── Ui/                      # GDI+ sparkline + constellation view
├── sdk/                         # minimal SDR# reference ("stub") assemblies
├── .github/workflows/build.yml  # CI: build + Release
└── docs/JSONL_FORMAT.md         # JSONL output schema
```

## Building

The plugin targets **.NET 8 (`net8.0-windows`), x86**. The current SDR# package
ships both `SDRSharp.dotnet8.exe` and `SDRSharp.dotnet9.exe` and the default is
the .NET 8 host (per the changelog, "reverted to dotnet 8.0 until the plugins
follow"); a `net8.0` plugin loads in **both** hosts, whereas a `net9.0` plugin
will not load in the .NET 8 host.

### GitHub Actions (recommended)

`.github/workflows/build.yml` builds the plugin on every push and attaches a
release asset whenever a `v*` tag is pushed (e.g. `git tag v1.0.0 &&
git push --tags`).

Modern SDR# is a single-file self-contained executable, so the managed
`SDRSharp.*` assemblies aren't shipped as separate DLLs. The plugin is compiled
against minimal **reference ("stub") assemblies** under [`sdk/`](sdk/) that mirror
the plugin API; at run time the references bind to SDR#'s real assemblies. See
[sdk/README.md](sdk/README.md).

### Local build

```sh
# build the reference stubs into ./libs, then the plugin
dotnet build sdk/SDRSharp.Radio/SDRSharp.Radio.csproj   -c Release
dotnet build sdk/SDRSharp.PanView/SDRSharp.PanView.csproj -c Release
dotnet build sdk/SDRSharp.Common/SDRSharp.Common.csproj  -c Release
mkdir -p libs && cp sdk/*/bin/Release/net8.0-windows/SDRSharp.*.dll libs/

dotnet build SDRSharp.JAlert/SDRSharp.JAlert.csproj -c Release /p:Platform=x86
```

(On a non-Windows host add `/p:EnableWindowsTargeting=true` to each — it compiles,
but the plugin only runs inside SDR# on Windows.)

## Installing into SDR#

The current SDR# auto-discovers plugins from its `Plugins/` directory (set by
`core.pluginsDirectory` in `SDRSharp.config`) — there is **no `Plugins.json`** and
no config edit needed.

1. Create a folder for the plugin inside SDR#'s `Plugins` directory and copy the
   DLL into it, e.g. `Plugins/SDRSharp.JAlert/SDRSharp.JAlert.dll`. (The release
   zip is already laid out this way — just extract it into `Plugins/`.)
2. Start SDR# (the default `SDRSharp.dotnet8.exe`). The plugin appears in the
   **Plugins** menu as **J-ALERT Decoder**.
3. **Play**, then tune onto the J-ALERT carrier. Make sure the decimation/zoom
   leaves at least ~400 kHz of IQ bandwidth around the carrier (the signal
   occupies ~346 kHz).

If the plugin doesn't appear, check `PluginError.log` in the SDR# folder for the
load error.

The panel shows lock state, a BPSK constellation, carrier/Costas offsets, the
estimated bit error rate, HDLC frame counts, and the latest decoded alert
(title / headline / time).

## Output

Recovered files can be:

- streamed as JSONL to a **file** and/or a **TCP** port — every recovered file,
  with its payload inline (the alert XML for gzipped JMA telegrams, or base64 of
  the raw bytes for non-XML telegrams) — see
  [docs/JSONL_FORMAT.md](docs/JSONL_FORMAT.md)
  ([日本語](docs/JSONL_FORMAT.ja.md)),
- written to an **Output folder** (enable with the **File output** checkbox):
  decoded JMA telegrams as `<timestamp>.xml`, other (non-XML) telegrams as the raw
  bytes in `<timestamp>.<chunk_type>.bin`.

Output paths default to `%APPDATA%\SDRSharp.JAlert\j_alert\` (the JSONL file to
`decoded.jsonl` there). All output settings persist in
`%APPDATA%\SDRSharp.JAlert\settings.json`.

## Platform support

| Target | Supported |
|---|---|
| Windows (SDR#, .NET 8, x86) | yes |
| Linux / macOS | no (SDR# is Windows-only) |

## License

MIT License — see [LICENSE](LICENSE). Copyright (c) 2026 KIRISHIKI Yudai.
