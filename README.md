# FH6 All-in-One Trainer

An all-in-one trainer for **Forza Horizon 6** — car/physics cheats, live SQL access to the game's in-memory database, weather control, XP grants, and runtime profile value hooks. Self-contained `.exe`, no .NET install needed.

> **Offline mode only.** This trainer modifies game memory. Online play (Rivals, Eventlab, Multiplayer, leaderboards) will not work and may result in a ban. Run FH6 in offline mode before using.

## Status

Current release: **v8.4.0** — download from [GitHub Releases](../../releases).

| Feature | Status |
|---|---|
| SQL cheats (cars, upgrades, prices) | works on all game builds tested |
| Profile hooks (Credits, Wheelspins, Skill Points, Drift, No Skill Break, Freeze AI, Sell Payout) | works on v403.798 and newer builds per user reports |
| Season switcher | Steam builds; newer builds pending reports after the v8.4.0 site fix |
| Weather control | experimental, first release in v8.4.0 |
| XP Grant | experimental, first release in v8.4.0 |
| Instant Rewards (wheelspin wallet write) | v403.798 |

**Profile hooks are protected by the CRC bypass.** The game periodically hashes its own code section and kills the process on mismatch; the trainer swaps the check's function pointer to a `ret` stub and re-arms it on a timer. Hooks install with plain external writes, and installation is exact-only: if the code bytes at a signature's site don't match, the cheat refuses instead of patching a guessed location. The Season listener is anchored by a long structured code signature verified against the string it references.

## Download

Get the `.zip` from **[GitHub Releases](../../releases)**, extract, and run `FH6AllInOneTrainer.exe` as Administrator. Check the title bar shows the version you downloaded.

## How to use

1. Start Forza Horizon 6 and **load fully into the world** (be driving, not in a menu).
2. Launch the trainer as Administrator. It attaches automatically.
3. Toggle the **Profile Values** on the Unlocks page, use **Instant Rewards**, **Weather** and **XP Grant**, or use the Database page for SQL cheats.
4. Change the season from the Unlocks page at any time.

> Enable cheats only once you are fully in-game. Offline mode only.

## Features

### Profile Values (Unlocks page, runtime hooks)
- **Credits** — set your credit balance (toggle on, then buy or sell something to refresh)
- **Wheelspins / Super Wheelspins** — set your spin counts (toggle on, then spin once)
- **Skill Points** — set your skill point count (toggle on, then spend one)
- **Drift Score Multiplier** — multiply drift scores (default 10x)
- **No Skill Break** — skill chains no longer break on impacts
- **Freeze AI** — AI cars stop updating their velocity
- **Sell Payout** — multiply car sell prices

### Instant Rewards (Unlocks page)
- Writes wheelspin / super-wheelspin counts directly into the reward wallet and shows your current counts

### Weather (Unlocks page, experimental)
- Live **Rain / Wetness / Atmosphere / Wind** intensities (0.0 to 1.0). Press Read to capture the current state, edit, Apply

### XP Grant (Unlocks page, experimental)
- Adds XP through the game's own progression system. First use: earn a little XP in game (any skill or race) so the trainer can find the XP system, then press Grant

### Quick Actions (Unlocks page)
- **Quick Start** — 999M Credits + Free Cars + Autoshow Unlock + Install Flags + All Cars
- **Max All** — max Credits, Wheelspins, Super Wheelspins, Skill Points

### Season (Unlocks page)
- Switch Spring / Summer / Autumn / Winter instantly. Click once before loading into the world so the listener is in place, then click again after loading

### SQL Database (Database page)
- **Unlock Everything** — all SQL cheats in one click
- Free Cars (BaseCost=0), Autoshow Unlock, Install Flags
- Add All Cars (CarBuckets), Free Upgrades (47 tables), Free Wheels, Full Autoshow
- Unlock Upgrade Presets, Clear "NEW!" Tag
- Persistent locks — Free Cars / Autoshow / Install Flags re-applied every 10 seconds

### Physics & Performance (Database page, SQL)
- Drift Score 10x, Max Traction (x3 per-car grip), Torque 2x, Reduce Drag 0.5x
- **Warning:** if the game saves while a physics cheat is active, the values get baked into your saved car tunes. Turn the lock off (Restore) before you switch cars or quit the game

## Known Limitations

- **Don't switch away from the game while playing offline.** When you alt-tab or minimize FH6 and come back, the game's own PlayFab cloud-save protection may shut it down on purpose. This is built into the game — it happens without the trainer too — and it is not a trainer crash. Keep the game in the foreground while playing.
- **Experimental hooks are disabled** (Time of Day, Gravity, Teleport, Acceleration, and the rest of the ForzaMods-ported set). They will return one at a time after a validation round. Skill Score Multiplier and Speed Trap Multiplier are permanently disabled — their signatures match too many code sites to hook safely.
- **Season and Weather depend on game internals** that move between builds. If a build is not recognized the cheat refuses with a clear message instead of guessing — report the message and your game version if you hit it.
- **Signature-based cheats** (SQL database AOBs, profile hook signatures) may need updating when Forza Horizon 6 patches.
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
