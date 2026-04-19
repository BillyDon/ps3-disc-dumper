# PS3 ISO Batch Dumper

A cross-platform CLI utility for decrypting and extracting PS3 game disc images (ISOs) in batch. Converts encrypted PS3 ISO files into decrypted game directories compatible with RPCS3 emulator.

**Built on**: [ps3-disc-dumper](https://github.com/13xforever/ps3-disc-dumper)  
**License**: Same as parent project

## Features

- ✅ **Batch Processing**: Dump multiple ISOs in one command
- ✅ **ZIP Support**: Automatically extracts ISO from ZIP archives
- ✅ **Automatic Key Finding**: Downloads IRD/Redump decryption keys
- ✅ **Cross-Platform**: Windows, macOS, Linux (.NET 10.0+)
- ✅ **Progress Tracking**: Real-time progress bars with ETAs
- ✅ **Error Resilience**: Continues batch on individual failures
- ✅ **RPCS3 Ready**: Outputs PS3_GAME directory structure

## Installation

### From Source
```bash
cd ~/ps3-disc-dumper
dotnet publish CLI --configuration Release --self-contained -o ~/ps3dump-release
```

Creates standalone executable: `~/ps3dump-release/CLI`

### Requires
- **.NET 10.0 SDK** (or Runtime 10.0 for self-contained builds)
- **Internet access** (to download IRD keys on first run)

## Usage

### Single ISO
```bash
CLI /path/to/game.iso --output ~/games --ird-cache ~/.ps3-iso-dumper/ird
```

### Batch from Directory
```bash
CLI ~/games/roms/ps3 --output ~/games/extracted
```
Processes all `.iso` and `.zip` files in directory (non-recursive).

### Batch with Custom Workers
```bash
CLI ~/games --output ~/output --workers 2
```
(Note: Batch processing is sequential by default; `--workers` is reserved for future parallel mode)

### Help
```bash
CLI --help
```

## Arguments

| Argument | Short | Default | Description |
|----------|-------|---------|-------------|
| `input` | — | required | ISO file, ZIP archive, or directory of ISOs/ZIPs |
| `--output` | `-o` | `./output` | Output directory for decrypted games |
| `--ird-cache` | — | `~/.ps3-iso-dumper/ird` | IRD key cache directory |
| `--workers` | `-w` | `1` | Parallel jobs (reserved, currently 1 only) |

## Output Structure

```
output/
└── GAME TITLE [PRODUCTCODE].ps3/
    └── PS3_GAME/
        ├── USRDIR/
        │   └── (game data files)
        ├── LICDIR/
        │   └── LIC.DAT
        └── PARAM.SFO
```

Each game is extracted to its own directory with `.ps3` extension for emulator compatibility. Compatible with:
- **RPCS3**: Point to parent `output/` directory
- **EmulationStation-based frontends**: `.ps3` extension auto-detected, rescan to populate

## IRD Cache

The CLI automatically:
1. **First Run**: Downloads IRD keys from three mirrors (flexby420, ps3ird.free.fr, ps3.aldostools.org)
2. **Subsequent Runs**: Uses cached keys, faster and offline-capable
3. **Fallback**: Uses embedded Redump database if online keys unavailable

IRD cache location: `~/.ps3-iso-dumper/ird/`

## Examples

### Linux/macOS: Batch dump from local directory
```bash
CLI \
  ~/roms/ps3 \
  --output ~/games/ps3_extracted \
  --ird-cache ~/.ps3-iso-dumper/ird
```

### Windows: Dump from USB drive
```powershell
.\CLI.exe D:\ps3_roms --output E:\ps3_extracted
```

### RPCS3 Integration
Point RPCS3 to the output directory:
1. RPCS3 → File → Add Games
2. Select `~/games/ps3_extracted`
3. RPCS3 scans `GAME TITLE [CODE]/PS3_GAME/PARAM.SFO` and displays games

## Troubleshooting

### "No ISO or ZIP files found"
- Check file extensions are `.iso` or `.zip` (case-insensitive)
- CLI doesn't recurse subdirectories; provide exact directory

### "No valid disc decryption key was found"
- Game is not in IRD/Redump databases (very rare)
- Delete `~/.ps3-iso-dumper/ird/*.ird` and re-run to re-download
- Manually add `.ird` file to cache (research required)

### "Missing file" warnings
- Original ISO may be corrupted or incomplete
- Continue with partial extraction; most files succeed

### Progress freezes
- USB JBOD is slow or in sleep; may take hours for large batches
- Check `ls /tmp` for extract temp directories (can be removed)

## Performance

**Single Game Time** (depends on USB/disk speed):
- Small game (2GB): ~3-5 minutes
- Large game (30GB+): ~30-60 minutes

**Batch Mode**: Process one at a time; run in background with `nohup` or `tmux`:
```bash
tmux new -s ps3dump
nohup ~/ps3dump-release/CLI /tank/media/Games/roms/ps3 --output /tank/media/Games/ps3_extracted &
```

## Cross-Platform Notes

### Linux/macOS
- File paths use `/` separators
- Cache location: `~/.ps3-iso-dumper/ird/`
- Fully supported

### Windows
- File paths use `\` or `/` (both work)
- Cache location: `%USERPROFILE%\.ps3-iso-dumper\ird\`
- Fully supported

### Older .NET
Requires **.NET 10.0** (released late 2024). Downgrade target-framework in `CLI.csproj` to `net8.0` or `net9.0` if needed (untested).

## Architecture

- **Dumper.cs** (Core): ISO detection, key finding, file decryption
- **IrdLibraryClient** (Library): IRD/Redump key fetching and caching
- **DiscUtils** (Dependency): ISO9660/UDF filesystem reading
- **CLI/Program.cs** (CLI): Argument parsing, batch coordination, progress display

ISO mode added via `Dumper.DetectIso()` — bypasses physical drive enumeration and uses `CDReader.OpenFile()` for per-file decryption.

## Limitations

- **No concurrent dumps** within single CLI invocation (I/O bound on USB)
- **ZIP files** are extracted to temp directory; original ZIPs not modified
- **Multi-disc games** (rare): Each disc extracted separately to same output directory
- **Trailing period filenames**: Handled automatically
- **Disc quirks** (mastering errors): Logged but don't block extraction

## Security

- IRD keys are **game identifiers only**, not license keys
- Decryption keys are **part of the ISO**, not derived from online sources
- No authentication, DRM bypass, or copying of protected content beyond intended ISO use

## Contributing

Issues and PRs welcome at: https://github.com/13xforever/ps3-disc-dumper

This CLI is an extension of the upstream project.

## License

Same as [ps3-disc-dumper](https://github.com/13xforever/ps3-disc-dumper) (see LICENSE in parent repo).
