# FH6 All-in-One Trainer

An all-in-one trainer for **Forza Horizon 6** — car/physics cheats, live SQL access to the game's in-memory database, and runtime profile value hooks. Self-contained `.exe`, no .NET install needed.

> **Offline mode only.** This trainer modifies game memory. Online play (Rivals, Eventlab, Multiplayer, leaderboards) will not work and may result in a ban. Run FH6 in offline mode before using.

## Status

The current release is **v8.1.0**.

**v8.0.0 restored the CRC bypass and re-enabled profile hooks.** Forza Horizon 6 periodically hashes its own code section (`.text`) and kills the process on any mismatch. The trainer swaps the CRC validation function pointer to a `ret` stub via in-process shellcode and re-arms it on a timer, so `.text` hooks survive the integrity scan. Profile value toggles (Credits, Wheelspins, Super Wheelspins, Skill Points) and physics hooks (Drift multiplier, No Skill Break, Sell Payout) work.

**v8.1.0** fixes the version display (a hardcoded AssemblyInfo pinned every build to v6.0.0 — the v8.0.0 zip actually contained a v6 binary), adds Season and Time of Day controls, and makes Super Grip double each car's own grip instead of a flat 10x that flipped cars in corners.

- **Profile value hooks** — Credits, Wheelspins, Super Wheelspins, Skill Points, Drift Score, No Skill Break, Sell Payout. Toggle ON; the CRC bypass protects the hooks automatically.
- **Memory Scanner** — crash-free value finder/setter. Scan for any in-game number and Set or Lock it. Version-independent (finds values by content, not fixed offsets).
- **SQL cheats** (Free Cars, Autoshow, Add All Cars, etc.) continue to work across all versions.
- **Instant Rewards** — locates the reward wallet and writes the wheelspin/super-wheelspin count directly (data write, no hooks).

## Download

Latest release: **[GitHub Releases](../../releases)** — download the `.zip`, extract, and run `FH6AllInOneTrainer.exe` as Administrator.

## How to use

1. Start Forza Horizon 6 and **load fully into the world** (be driving, not in a menu).
2. Launch the trainer as Administrator and attach.
3. To edit money/spins/points: toggle the **Profile Values** on (Credits, Wheelspins, etc.), or use the **Memory Scanner** for any other integer. For the scanner, enter your exact current value, click **Find Value**, narrow with Next Scan if needed, then **Set** (or **Lock** to keep it applied).
4. SQL cheats (cars, upgrades, etc.) are on the Database tab — one click.

> Enable cheats only once you are fully in-game. Offline mode only.

## Features

### SQL Database (in-memory SQLite)
- **Unlock Everything** — all SQL cheats in one click
- Free Cars (BaseCost=0), Autoshow Unlock, Install Flags
- Add All Cars (CarBuckets approach), Free Upgrades (47 tables), Free Wheels, Full Autoshow
- Unlock Upgrade Presets, Clear "NEW!" Tag

### Physics & Performance (SQL)
- Drift Score 10x, Max Traction, Torque 2x, Reduce Drag 0.5x

### Memory Scanner (crash-free)
- **Find Value** — enter your current in-game number; the trainer scans all writable memory for it.
- Narrow with **Next Scan** filters (Exact / Increased / Decreased / Changed / Unchanged) when there are multiple matches.
- Set a value once, or **Lock** it to keep re-applying. Make an address **Permanent** by discovering a static pointer chain to it.
- Works for Credits, Wheelspins, Super Wheelspins, Skill Points, XP, and any integer.

### Profile Values & Physics (runtime hooks — CRC-protected)
- Credits, Wheelspins, Super Wheelspins, Skill Points, Drift Score Multiplier, No Skill Break, Sell Payout.
- Toggle ON to enable. The CRC bypass protects these hooks automatically.
- Instant Rewards locates the reward wallet and writes wheelspin counts directly (data write, no hooks).

### Quick Actions
- **Quick Start** — 999M Credits + Free Cars + Autoshow Unlock + Install Flags + All Cars
- **Max All** — max Credits, Wheelspins, Super Wheelspins, Skill Points

### World
- **Season** — switch Spring / Summer / Autumn / Winter instantly
- **Time of Day** — set any hour (0.0 to 24.0) with presets and lock it

## Known Limitations

- **Profile hooks depend on the CRC bypass.** The bypass swaps the CRC validation function pointer to a `ret` stub and re-arms it on a timer. If a future game update moves the CRC signature or changes the integrity mechanism, hooks may need re-signature work. The Memory Scanner and SQL cheats avoid `.text` entirely and are unaffected.
- **Signature-based cheats** (SQL database AOBs, profile hook signatures) may need updating when Forza Horizon 6 patches. The Memory Scanner is version-independent (finds values by content).
- **Offline mode only.** This trainer modifies game memory; online play (Rivals, Eventlab, Multiplayer, leaderboards) will not work and may result in a ban.

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
| **[Chaarkor](https://github.com/Chaarkoor)** | Original Avalonia UI shell, MVVM architecture |
| **[changcheng967](https://github.com/changcheng967)** | All-in-one integration, physics SQL cheats, in-process hook installation, UI |

## License

GPL-3.0 — see [LICENSE](LICENSE).
