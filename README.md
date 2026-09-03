# FH6 All-in-One Trainer

An all-in-one trainer for **Forza Horizon 6** — car/physics cheats, live SQL access to the game's in-memory database, and runtime profile value hooks. Self-contained `.exe`, no .NET install needed.

> **Offline mode only.** This trainer modifies game memory. Online play (Rivals, Eventlab, Multiplayer, leaderboards) will not work and may result in a ban. Run FH6 in offline mode before using.

## Status

Current release: **v8.1.2** — download from [GitHub Releases](../../releases).

| Subsystem | Status |
|---|---|
| SQL cheats (cars, upgrades, prices) | works on all game builds tested |
| Memory Scanner (find / set / lock any value) | works on all game builds tested |
| Profile hooks (Credits, Wheelspins, Skill Points, Drift, …) | works on v403.798 — v420.696 pending field reports |
| Season switcher | Steam builds |
| Instant Rewards (wheelspin wallet write) | v403.798 |

**Profile hooks depend on the CRC bypass.** Forza Horizon 6 periodically hashes its own code section (`.text`) and kills the process on any mismatch. The trainer swaps the CRC validation function pointer to a `ret` stub and re-arms it on a timer, so hooks survive the integrity scan. Hooks are installed with plain external writes, and installation is exact-only: if the bytes at a signature's site don't match, the cheat refuses instead of patching a guessed site.

## Download

Get the `.zip` from **[GitHub Releases](../../releases)**, extract, and run `FH6AllInOneTrainer.exe` as Administrator. Check the title bar shows the version you downloaded.

## How to use

1. Start Forza Horizon 6 and **load fully into the world** (be driving, not in a menu).
2. Launch the trainer as Administrator and attach.
3. To edit money/spins/points, toggle the **Profile Values** on, or use the **Memory Scanner** for any other integer: enter your exact current value, click **Find Value**, narrow with Next Scan if needed, then **Set** (or **Lock** to keep it applied).
4. SQL cheats (cars, upgrades, etc.) are on the Database tab — one click.

> Enable cheats only once you are fully in-game. Offline mode only.

## Features

### SQL Database (in-memory SQLite)
- **Unlock Everything** — all SQL cheats in one click
- Free Cars (BaseCost=0), Autoshow Unlock, Install Flags
- Add All Cars (CarBuckets approach), Free Upgrades (47 tables), Free Wheels, Full Autoshow
- Unlock Upgrade Presets, Clear "NEW!" Tag

### Physics & Performance (SQL)
- Drift Score 10x, Max Traction (x3 per-car grip), Torque 2x, Reduce Drag 0.5x

### Memory Scanner (crash-free)
- **Find Value** — enter your current in-game number; the trainer scans all writable memory for it
- Narrow with **Next Scan** filters (Exact / Increased / Decreased / Changed / Unchanged)
- Set a value once, or **Lock** it to keep re-applying; make an address **Permanent** with a static pointer chain
- Works for Credits, Wheelspins, Super Wheelspins, Skill Points, XP, and any integer

### Profile Values (runtime hooks — CRC-protected)
- Credits, Wheelspins, Super Wheelspins, Skill Points, Drift Score Multiplier, No Skill Break, Sell Payout
- **Instant Rewards** — writes wheelspin / super-wheelspin counts directly into the reward wallet (data write, no hooks)

### Quick Actions
- **Quick Start** — 999M Credits + Free Cars + Autoshow Unlock + Install Flags + All Cars
- **Max All** — max Credits, Wheelspins, Super Wheelspins, Skill Points

### World
- **Season** — switch Spring / Summer / Autumn / Winter instantly

## Known Limitations

- **Experimental hooks are disabled** (Time of Day, Gravity, Teleport, Acceleration, and the rest of the ForzaMods-ported set). They will return one at a time after a validation round. Skill Score Multiplier and Speed Trap Multiplier are permanently disabled — their signatures match too many code sites to hook safely.
- **Signature-based cheats** (SQL database AOBs, profile hook signatures) may need updating when Forza Horizon 6 patches. The Memory Scanner is version-independent (finds values by content).
- **Offline mode only.** Online play will not work and may result in a ban.

## Build from Source

Requires **.NET 10 SDK** on Windows:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Credits

| Who | Contribution |
|-----|-------------|
| **[paris' club](https://discord.gg/WSd3bRNJuJ)** | Core profile cheats (CALL-resolution approach), SQL features |
| **[ForzaMods](https://github.com/ForzaMods/Forza-Mods-AIO)** | AOB signatures reference |
| **[matkhl](https://www.unknowncheats.me/forum/other-games/752793)** | Free Upgrades SQL (47 tables), CarBuckets approach, database dumper |
| **[Omkmakwana](https://github.com/Omkmakwana/FH6Trainer)** | Add All Cars reference |
| **[Chaarkor](https://github.com/Chaarkoor)** | Original Avalonia UI shell, MVVM architecture, hook engine |
| **[changcheng967](https://github.com/changcheng967)** | All-in-one integration, physics SQL cheats, UI |

## License

GPL-3.0 — see [LICENSE](LICENSE).
