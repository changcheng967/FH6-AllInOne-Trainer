# FH6 All-in-One Trainer / FH6 全能修改器

> **English** | [**简体中文**](#简体中文)

---

<a id="简体中文"></a>

# FH6 全能修改器

**Forza Horizon 6** 全能修改器 — 运行时挂钩玩家档案数值 + 车辆/物理作弊 + 实时 SQL 访问游戏内存数据库。单文件 `.exe`，无需安装 .NET 运行库。

> **仅离线模式。** 本修改器会修改游戏内存。联机游玩（Rivals、Eventlab、多人、排行榜）将无法正常工作，并可能导致封号。使用前请将 FH6 切换至离线模式。

---

## 下载 / Download

| 语言 | 说明 |
|------|------|
| **English** | Latest release: **[GitHub Releases](../../releases)** — download the `.zip`, extract, and run `FH6AllInOneTrainer.exe` as **Administrator**. |
| **简体中文** | 最新发布：[**GitHub Releases**](../../releases) — 下载 `.zip`，解压后**以管理员身份**运行 `FH6AllInOneTrainer.exe`。 |

---

## 功能 / Features

### 档案数值（运行时挂钩）/ Profile Values (runtime hooks)

| English | 简体中文 |
|---------|----------|
| **Credits** — set any amount (10K to 999M). Toggle on, then buy/sell something for the change to take effect. | **信用点** — 可设任意金额（1万~9.99亿）。开启后购买或出售以生效。 |
| **Wheelspins** — set count (10–999). Toggle on, then spin once for it to lock. | **轮盘抽奖** — 可设数量（10~999）。开启后转一次以锁定。 |
| **Super Wheelspins** — set count (10–999). **Enable Wheelspins first**, then toggle SWS on and spin once to activate. | **超级轮盘抽奖** — 可设数量（10~999）。**先启用轮盘抽奖**，再开启此功能并转一次。 |
| **Skill Points** — set any amount. Toggle on, then spend a point for the change to take effect. | **技能点** — 可设任意数量。开启后消耗一个技能点以生效。 |
| **Sell Payout** — multiply car sell prices by any value. | **卖车收益** — 乘以任意倍数的卖车价格。 |
| **Drift Score** — multiply drift score by any value (5x, 10x, 50x, or custom). | **漂移分数** — 乘以任意倍数（5倍、10倍、50倍或自定义）。 |
| **No Skill Break** — prevents skill chains from breaking on impacts. | **技能连击不中断** — 防止技能连击因撞击中断。 |

> **Tip / 提示:** Wheelspins must be enabled for Super Wheelspins (and some other cheats) to take effect. Enable Wheelspins first, then add your other cheats. / 轮盘抽奖必须先启用，超级轮盘抽奖（及部分其他作弊）才能生效。先开启轮盘抽奖，再添加其他作弊。

Uses inline code cave hooks with toggle+value slots — based on the paris' club approach (CALL-resolution with string-compare verification). / 采用内联代码洞穴挂钩 + 开关/值槽 — 基于 paris' club 方案（CALL 解析 + 字符串比较验证）。

### 快速操作 / Quick Actions

| English | 简体中文 |
|---------|----------|
| **Quick Start** — 999M Credits + Free Cars + Autoshow Unlock + Install Flags + All Cars | **快速上手** — 9.99亿信用点 + 免费车辆 + 展厅解锁 + 安装标志 + 全部车辆 |
| **Max All** — max Credits, Wheelspins, Super Wheelspins, Skill Points | **全部拉满** — 最大信用点、轮盘抽奖、超级轮盘抽奖、技能点 |

### SQL 数据库（内存 SQLite）/ SQL Database (in-memory SQLite)

| English | 简体中文 |
|---------|----------|
| **Unlock Everything** — all SQL cheats in one click | **解锁全部** — 一键执行所有 SQL 作弊 |
| Free Cars (BaseCost=0), Autoshow Unlock, Install Flags — with persistent locks that re-apply every 10s | 免费车辆（BaseCost=0）、展厅解锁、安装标志 — 支持持久锁定（每10秒重新应用） |
| Add All Cars (CarBuckets approach), Free Upgrades (47 tables), Free Wheels, Full Autoshow | 添加所有车辆（CarBuckets 方案）、免费升级（47个表）、免费轮毂、完整展厅 |
| Unlock Upgrade Presets, Clear "NEW!" Tag | 解锁升级预设、清除 "NEW!" 标记 |

### 物理与性能（SQL）/ Physics & Performance (SQL)

| English | 简体中文 |
|---------|----------|
| Drift Score 10x, Max Traction, Torque 2x, Reduce Drag 0.5x | 漂移分数10倍、最大抓地力、扭矩2倍、减少阻力0.5倍 |

---

## 反作弊绕过 / Anti-Cheat Bypass

| English | 简体中文 |
|---------|----------|
| CRC bypass with heartbeat timer + jitter (XXH check pointer replacement) | CRC 绕过 + 心跳定时器 + 抖动（XXH 检查指针替换） |
| 7/7 integrity check patches (MemCmp, PageHash, TextHash, CodeSection, Checksum, TerminateGuard, ResumeReboot) | 7/7 完整性检查补丁 |
| **TerminateGuard** — patches the conditional `TerminateProcess` call that caused ~10 minute auto-shutdown | **TerminateGuard** — 修补导致约10分钟自动关闭的条件 `TerminateProcess` 调用 |
| **ResumeReboot** — prevents GamePass/Windows Store silent reboot on alt-tab | **ResumeReboot** — 防止 GamePass/Windows Store 在切屏时静默重启 |
| IAT shutdown hooks — replaces `TerminateProcess` and `ExitProcess` IAT entries with stubs | IAT 关闭挂钩 — 用存根替换 `TerminateProcess` 和 `ExitProcess` 的 IAT 条目 |
| Per-hook error handling — if one hook fails, other cheats continue working | 逐挂钩错误处理 — 单个挂钩失败不影响其他作弊 |
| Thread-safe patching with ExpectedOriginal sanity check | 线程安全补丁 + ExpectedOriginal 完整性检查 |
| Pre-resolution: all hook targets are scanned before any hooks are installed | 预解析：所有挂钩目标在安装前预先扫描 |

---

## 已知限制 / Known Limitations

| English | 简体中文 |
|---------|----------|
| **XP / Level modding** is not yet supported. See [issue #19](../../issues/19) for discussion. | **经验值/等级修改** 暂不支持。参见 [issue #19](../../issues/19)。 |
| **Wheelspins dependency** — Super Wheelspins (and possibly Credits) require Wheelspins to be enabled first. | **轮盘依赖** — 超级轮盘抽奖（及可能的信用点）需要先启用轮盘抽奖。 |

---

## 从源码构建 / Build from Source

Requires **.NET 10 SDK** on Windows / 需要 Windows 上的 **.NET 10 SDK**：

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

---

## 本地化 / Localization

本修改器内置 **12 种语言**支持，可在运行时从 **设置 → 语言** 切换，无需重启。 / The trainer ships with built-in support for **12 languages**, switchable at runtime from **Settings → Language** without restarting.

| 文件 / File | 语言 / Language | 本地名称 / Native name |
|-------------|-----------------|------------------------|
| `en.json` | English | English |
| `zh-CN.json` | Chinese (Simplified) | 简体中文 |
| `zh-TW.json` | Chinese (Traditional) | 繁體中文 |
| `ja.json` | Japanese | 日本語 |
| `ko.json` | Korean | 한국어 |
| `es.json` | Spanish | Español |
| `fr.json` | French | Français |
| `de.json` | German | Deutsch |
| `pt.json` | Portuguese | Português |
| `it.json` | Italian | Italiano |
| `ru.json` | Russian | Русский |
| `ar.json` | Arabic | العربية |

> Translations are loaded from `Localization/*.json` alongside the executable. To add or improve a translation, edit the corresponding `.json` file — no recompilation needed. Missing keys fall back to English, then to the raw key name. / 翻译文件位于可执行文件旁的 `Localization/*.json`。要添加或改进翻译，直接编辑对应的 `.json` 文件即可 — 无需重新编译。缺失的键会回退到英文，再回退到原始键名。

---

## 致谢 / Credits

| 贡献者 / Who | 贡献 / Contribution |
|--------------|---------------------|
| **[paris' club](https://discord.gg/WSd3bRNJuJ)** | Core profile cheats, SQL features, CRC bypass / 核心档案作弊、SQL 功能、CRC 绕过 |
| **[ForzaMods](https://github.com/ForzaMods/Forza-Mods-AIO)** | AOB signatures reference / AOB 特征码参考 |
| **[matkhl](https://www.unknowncheats.me/forum/other-games/752793)** | Free Upgrades SQL (47 tables), CarBuckets approach / 免费升级 SQL、CarBuckets 方案 |
| **[Omkmakwana](https://github.com/Omkmakwana/FH6Trainer)** | Add All Cars reference / 添加所有车辆参考 |
| **[Chaarkor](https://github.com/Chaarkoor)** | Original Avalonia UI shell, MVVM architecture / 原始 Avalonia UI 框架、MVVM 架构 |
| **[changcheng967](https://github.com/changcheng967)** | All-in-one integration, physics SQL cheats, code cave detours, UI / 全能集成、物理 SQL 作弊、代码洞穴、UI |

---

## 许可协议 / License

GPL-3.0 — 详见 [LICENSE](LICENSE)。
