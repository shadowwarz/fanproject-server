# Durango Fan Server — Code Analysis & Fix Instructions

**Prepared by:** Computer (MuleRun)
**Date:** 2026-10-18
**Target:** This document is for developer "antigravity" to action.

---

## Summary

The server compiles and runs correctly. Core gameplay is functional (auth, world, terrain, assets).
Four categories of issues need fixing, ordered by priority:

| # | Category | Severity | Files affected |
|---|----------|----------|----------------|
| 1 | Admin dashboard blocked from remote browsers | High | `Core/Gateway.cs` |
| 2 | Hardcoded Windows paths in APK routes | High | `Core/Gateway.cs` |
| 3 | All log/comment text in Thai (9,317 occurrences) | Medium | 142 `.cs` files |
| 4 | README and inline docs in Thai | Low | `README.md`, `Program.cs`, all Core files |

---

## Issue 1 — Admin Dashboard Blocked (HIGH)

### Problem

The admin dashboard at `http://<host>:8190/admin` loads in the browser but every API call
returns `403 Forbidden`. This happens because the server was started without `--admin-token`,
which causes `IsAdminAllowed()` to allow only loopback requests (`127.0.0.1`).

Any browser connecting from outside the machine hits 403 on every endpoint:
`/health`, `/admin/who`, `/admin/config`, `/admin/economy`, etc.

### Root Cause

`server/Core/Gateway.cs` line 835–858:

```csharp
private bool IsAdminAllowed(HttpListenerRequest request)
{
    string want = AdminToken;
    if (string.IsNullOrEmpty(want))
    {
        // No token set → only loopback allowed
        IPAddress from = request?.RemoteEndPoint?.Address;
        return from != null && IPAddress.IsLoopback(from);
    }
    // Token set → check ?token= or X-Admin-Token header
    string got = request?.QueryString?["token"] ?? request?.Headers?["X-Admin-Token"];
    ...
}
```

### Fix — Two parts

#### Part A: Set admin token when starting the server

Start the server with an admin token either via environment variable (recommended) or CLI flag:

```bash
# Option 1 — environment variable (does not appear in `ps` output)
export DURANGO_ADMIN_TOKEN=your-secret-token
./tools/deploy_to_mulerun.sh start

# Option 2 — CLI flag (visible in ps)
./DurangoServer --admin-token your-secret-token ...
```

On the running Mulerun VPS, stop and restart:

```bash
cd ~/fanproject-server
./tools/deploy_to_mulerun.sh stop
export DURANGO_ADMIN_TOKEN=changeme123
./tools/deploy_to_mulerun.sh start
```

#### Part B: Update deploy script to read the env variable

The `tools/deploy_to_mulerun.sh` already reads `DURANGO_ADMIN_TOKEN` and passes it as
`--admin-token` when it is set. No script changes needed — just set the env variable before
calling the script.

#### Part C: Document token in README

Add a section to `README.md` explaining how to set the admin token so operators know
the admin dashboard requires it.

### Verification

After restart with a token:

```bash
curl "http://127.0.0.1:8190/health?token=changeme123"
# Should return JSON with uptime, tick stats, player count
```

From a browser: open `http://<PUBLIC_IP>:8190/admin`, enter `http://<PUBLIC_IP>:8190`
as Server URL and the token value, click Sign In.

---

## Issue 2 — Hardcoded Windows Paths (HIGH)

### Problem

`server/Core/Gateway.cs` contains four hardcoded absolute Windows paths from a developer's
local machine. These routes fail silently (return 404) on Linux. The paths are:

| Line | Route | Hardcoded path |
|------|-------|----------------|
| 141 | `GET /apk/wifi` | `C:\Users\mfadi\Downloads\Fanprojects\Durango-Android-WiFi.apk` |
| 148 | `GET /apk/online` | `C:\Users\mfadi\Downloads\Fanprojects\Durango-Android-Online.apk` |
| 1180 | Android bundle fallback | `C:\Users\mfadi\Downloads\Fanprojects\Fanprojects-OffServer\DurangoV2_Data\StreamingAssets\AssetBundles` |
| 1187 | Android bundle fallback 2 | `C:\Users\mfadi\Downloads\Fanprojects\newuniverse\Assets\StreamingAssets\AssetBundles` |
| 1277 | Windows bundle fallback | same as 1180 |
| 1281 | Windows bundle fallback 2 | same as 1187 |

### Fix

Replace hardcoded paths with relative paths based on the binary directory or a CLI flag.

#### `/apk/wifi` and `/apk/online` routes (lines 139–151)

```csharp
// BEFORE
_webServer.GetRoute["/apk/wifi"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
{
    string p = @"C:\Users\mfadi\Downloads\Fanprojects\Durango-Android-WiFi.apk";
    if (File.Exists(p)) return new WebServer.FileResponse(p);
    return new WebServer.NotFountResponse();
};

_webServer.GetRoute["/apk/online"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
{
    string p = @"C:\Users\mfadi\Downloads\Fanprojects\Durango-Android-Online.apk";
    if (File.Exists(p)) return new WebServer.FileResponse(p);
    return new WebServer.NotFountResponse();
};

// AFTER — resolve relative to binary directory
_webServer.GetRoute["/apk/wifi"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
{
    string p = Path.Combine(AppContext.BaseDirectory, "apk", "Durango-Android-WiFi.apk");
    if (File.Exists(p)) return new WebServer.FileResponse(p);
    return new WebServer.NotFountResponse();
};

_webServer.GetRoute["/apk/online"] = (HttpListenerRequest request, Dictionary<string, string> _) =>
{
    string p = Path.Combine(AppContext.BaseDirectory, "apk", "Durango-Android-Online.apk");
    if (File.Exists(p)) return new WebServer.FileResponse(p);
    return new WebServer.NotFountResponse();
};
```

Place APK files in `<server-binary-dir>/apk/` (e.g. `~/durango/linux-x64/apk/`).

#### Bundle fallback paths (lines ~1177–1190 and ~1274–1284)

Remove the hardcoded Windows candidate paths entirely. The bundle directory is already
configurable via `--assetbundles-android` CLI flag. The fallback candidates should only
check paths relative to the binary or the data directory:

```csharp
// Remove these two candidates entirely:
// string candidate2 = @"C:\Users\mfadi\Downloads\...";
// string candidate3 = @"C:\Users\mfadi\Downloads\...";

// Keep only relative candidates that work cross-platform:
string candidate1 = Path.Combine(AppContext.BaseDirectory, "AssetBundles", "Android");
string candidate2 = Path.Combine(AppContext.BaseDirectory, "live", "android");
```

---

## Issue 3 — Thai Language Log Messages (MEDIUM)

### Problem

All `Console.WriteLine` log messages throughout the server are written in Thai.
This makes log monitoring, search, and handoff to non-Thai developers very difficult.

There are **9,317** Thai-character occurrences across **142 C# files**.
A further **126** Korean-character occurrences exist (likely game data strings carried over
from Nexon source).

Key files with the most log messages (priority targets):

| File | Thai log lines | Purpose |
|------|---------------|---------|
| `Core/GameServer.cs` | ~40 | Auth, session, TCP connection |
| `Core/Gateway.cs` | ~80 | HTTP routes, admin API |
| `Core/Host.cs` | ~30 | Server lifecycle, startup |
| `Program.cs` | ~25 | CLI, boot sequence |
| `Support/AppData.cs` | ~20 | Save/load |
| `Support/SafeSave.cs` | ~15 | File persistence |
| `Support/WorldRegistry.cs` | ~15 | Island/world management |
| `Support/RegionCatalog.cs` | ~10 | Region loading |
| `Support/QuestCatalog.cs` | ~10 | Quest loading |
| `SelfTest.cs` | ~30 | Self-test output |

### Fix Strategy

Replace all Thai `Console.WriteLine` strings with English equivalents.
Do NOT translate game data strings (item names, quest text, etc.) — those are player-facing
content and intentionally Thai. Only translate developer-facing log messages.

#### Pattern to search and replace

```bash
# Find all Thai Console.WriteLine calls
grep -rn --include="*.cs" -P "Console\.WriteLine.*[\x{0E00}-\x{0E7F}]" server/
```

#### Representative translations for common patterns

| Thai log (approximate) | English replacement |
|------------------------|---------------------|
| `[auth] ตัดสายที่ไม่ผ่าน Auth ภายในเวลา` | `[auth] Dropped connection: auth timeout` |
| `[auth] ปฏิเสธ: token ไม่ตรงกับ entity` | `[auth] Rejected: token does not match entity` |
| `[auth] ปฏิเสธ: ไม่รู้จักตัวละคร` | `[auth] Rejected: unknown character` |
| `[auth] ปฏิเสธสายใหม่ — เต็มเพดาน` | `[auth] Rejected new connection: at capacity` |
| `[auth] ปฏิเสธการผูก session: บัญชีไม่ใช่เจ้าของตัวละคร` | `[auth] Rejected session bind: account is not owner of character` |
| `[boot] ปิดเซิร์ฟ (ปิดโปรเซส) — เซฟก่อน...` | `[boot] Shutting down — saving first...` |
| `[boot] เซฟเรียบร้อย` | `[boot] Save complete` |
| `[boot] พร้อม — gateway … · game …` | `[boot] Ready — gateway ... game ...` |
| `[tcp] Client connected from` | (already English — keep) |
| `[radiotower] สายแชท/สังคมต่อติด` | `[radiotower] Chat/social connection accepted` |
| `[welcome] … Region.Id=…` | (already mixed — translate Thai parts) |
| `[เวอร์ชัน] ปฏิเสธตัวเกม …` | `[version] Rejected client version` |
| `[data] โหลด game data จาก … เสร็จ` | `[data] Loaded game data from ... done` |
| `[region] สารบัญเกาะ … ลูก` | `[region] Island catalog ... entries` |
| `[host] สร้างโลกใหม่` | `[host] Creating new world` |
| `[host] cluster 'nx': ผู้เล่น … สล็อต` | `[host] cluster 'nx': ... player slots` |
| `[สัตว์] โหลดชนิดสัตว์ …` | `[animals] Loaded ... animal types` |
| `[craft] โหลดสูตรคราฟต์ …` | `[craft] Loaded ... crafting recipes` |

#### Approach for antigravity

1. Open each file listed in the priority table above.
2. For every `Console.WriteLine(...)` or `Console.Write(...)` containing Thai text,
   translate the **string literal** to English. Do not change variable names, method names,
   or game data values.
3. XML doc comments (`/// <summary>`) in Thai can be translated in a second pass —
   they don't affect runtime behavior.
4. Use the grep command above to verify Thai count drops to 0 in Console output lines.

---

## Issue 4 — README and Documentation in Thai (LOW)

### Problem

`README.md`, `Program.cs` header comment, and XML `<summary>` doc comments in all
Core and Support files are written in Thai. This blocks non-Thai contributors.

### Fix

Translate `README.md` to English (full rewrite).
Translate XML doc comments in the following files (highest value):

- `Core/GameServer.cs` — session auth design comments are important
- `Core/Gateway.cs` — route design decisions
- `Core/Host.cs` — server lifecycle
- `Program.cs` — CLI argument docs

---

## Admin Dashboard — Current Status & Known Gaps

The admin dashboard HTML/CSS/JS (`server/admin/`) is well-built and covers:

- Dashboard with live stats (players, uptime, tick rate, save health)
- Config Editor (reads/writes `config.json` hot-reload)
- Feature Toggles (reads from `config.json` Features section)
- Island Management
- Player Management (online list, whitelist, ban list)
- Economy monitor (T Stone supply, wealth distribution)
- Game Data Browser (prototypes, recipes, animals, buildings)
- Server Actions (broadcast, maintenance mode, config reload)

### Missing backend endpoint

The `GET /admin/features` and `POST /admin/features` routes do NOT exist in `Gateway.cs`.
The Feature Toggles page works around this by loading from `/admin/config` and editing
the `Features` sub-object — this is functional but not immediately obvious.
No fix required unless a dedicated features endpoint is desired.

### `unban` not exposed in UI

`POST /admin/unban` exists in `Gateway.cs` (line 460) but there is no Unban button in the
Ban List tab of the Players page. Add an Unban button next to each row in `app.js`.

---

## Quick Reference — Live Server

| Item | Value |
|------|-------|
| Public IP | 47.79.240.13 |
| Gateway URL | http://47.79.240.13:8190 |
| Admin Dashboard | http://47.79.240.13:8190/admin |
| Game TCP port | 8191 |
| Server PID | check with `cat ~/fanproject-server/server/publish/linux-x64/durango.pid` |
| Logs | `~/fanproject-server/server/publish/linux-x64/server.log` |
| Client config | `offserver.txt` → `gateway=http://47.79.240.13:8190` |

### Restart with admin token

```bash
cd ~/fanproject-server
./tools/deploy_to_mulerun.sh stop
export DURANGO_ADMIN_TOKEN=your-secret-token
./tools/deploy_to_mulerun.sh start
# Then open http://47.79.240.13:8190/admin and sign in with the token
```

---

## File Map for antigravity

```
fanproject-server/
  server/
    Core/
      Gateway.cs        ← Issue 1 (admin token), Issue 2 (hardcoded paths), Issue 3 (Thai logs)
      GameServer.cs     ← Issue 3 (Thai logs + comments)
      Host.cs           ← Issue 3 (Thai logs + comments)
    Program.cs          ← Issue 3 (Thai logs), Issue 4 (Thai header comment)
    admin/
      index.html        ← OK (English, no changes needed)
      app.js            ← Minor: add Unban button (see Issue 4 gap)
      style.css         ← OK
  README.md             ← Issue 4 (entirely Thai)
  tools/
    deploy_to_mulerun.sh ← OK, reads DURANGO_ADMIN_TOKEN already
```
