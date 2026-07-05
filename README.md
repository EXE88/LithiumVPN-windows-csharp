<div align="center">

# ⚡ LithiumVPN

**A modern, fluent Windows VPN client built with WinUI 3 and powered by the Xray core.**

Connect securely, manage your subscription, buy plans with coins, and get support — all from one beautifully designed native app, in English or Persian.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET%208-WinUI%203-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Core](https://img.shields.io/badge/core-Xray%2026.x-00A98F)](https://github.com/XTLS/Xray-core)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

</div>

---

## ✨ Overview

**LithiumVPN** is a self‑contained desktop VPN client for Windows 10/11. It pairs a polished **WinUI 3 (Windows App SDK)** interface with the high‑performance **[Xray](https://github.com/XTLS/Xray-core) core** to give users a fast, reliable, and censorship‑resistant connection.

The app talks to an **XUI‑Accounter** backend, so it is far more than a tunnel: users can sign in, view their subscriptions and remaining data, purchase plans with in‑app coins, browse servers, ping them, and open support tickets — without ever leaving the app. Everything runs **per‑user with no admin rights required**, using Windows system‑proxy mode rather than a TUN driver.

## 🎯 Features

### Connectivity
- 🔒 **Xray‑powered tunnel** — supports `VLESS`, `VMess`, `Trojan`, `Shadowsocks`, `SOCKS`, `HTTP` and raw JSON configs across all modern transports.
- 🌐 **System‑proxy mode** — no kernel driver, no admin prompt; the app configures WinINET and cleanly restores it on exit (and even after a crash).
- 📡 **Live connection health** — the tunnel is verified before the proxy is enabled, with real‑time latency and cumulative upload/download traffic shown on the dashboard.
- 🚦 **Per‑server ping** — measure real latency to each server through a temporary probe core before you connect.

### Account & Subscriptions
- 👤 **Full account system** — register, e‑mail verification, login, and logout against the backend API.
- 💳 **Plans & coins** — browse available plans and purchase them with your coin balance, all in‑app.
- 📊 **Subscription insight** — remaining data (GB), days left, and per‑config usage rings, kept fresh automatically.
- 🎫 **Built‑in support** — create tickets and chat with support (client/admin bubbles) right inside the app.
- 🔔 **Notifications** — recent account events with unread badges.

### Experience
- 🌍 **Runtime localization** — switch between **English** and **Persian (فارسی)** instantly, with full **RTL** layout support — no restart.
- 🎨 **Fluent design** — light/dark themes and Acrylic/Mica window backdrops via DevWinUI.
- 🖥️ **System tray integration** — closing hides to the tray; quick Disconnect/Open/Exit from the tray menu.
- 🧭 **Guided tour** — a first‑run walkthrough of the dashboard.
- 🔄 **Background heartbeat** — status, events, and tickets refresh periodically and the UI re‑renders live.

## 🖼️ Screenshots

> _Add screenshots of the Dashboard, Servers, Plans and Settings pages here._

<!--
| Dashboard | Servers | Plans |
|-----------|---------|-------|
| ![Dashboard](docs/dashboard.png) | ![Servers](docs/servers.png) | ![Plans](docs/plans.png) |
-->

## 🏗️ Architecture

```
Lithiumvpn/
├─ App.xaml(.cs)            # App bootstrap, tray icon, theme, single-window host
├─ MainWindow.xaml(.cs)     # Navigation shell (Frame + NavigationView)
├─ Pages/                   # Splash, Login, Dashboard, Servers, Plans,
│                           #   Account, Coins, Notifications, Help, Settings
├─ Dialogs/                 # ConfigSelectionDialog, …
├─ Services/
│  ├─ ApiClient.cs          # HttpClient wrapper (bearer + refresh-on-401 + retry)
│  ├─ ApiService.cs         # Typed facade over every backend endpoint
│  ├─ ApiModels.cs          # Envelope + DTOs
│  ├─ TokenStore.cs         # Access/refresh tokens persisted per-user
│  ├─ AppState.cs           # In-memory cache of the signed-in user's data
│  ├─ AppConfig.cs          # Reads .env (API_BASE_URL, timeouts, …)
│  ├─ HeartbeatService.cs   # Periodic backend refresh
│  ├─ TrayIconService.cs    # Raw Shell_NotifyIcon P/Invoke (no packages)
│  └─ Xray/
│     ├─ XrayLinkParser.cs      # Parse share URIs → Xray config
│     ├─ XrayConfigBuilder.cs   # Build SOCKS/HTTP inbound config
│     ├─ XrayProcess.cs         # Spawn / monitor the core
│     ├─ ConnectionService.cs   # Singleton orchestrator (connect/verify/traffic)
│     ├─ SystemProxyService.cs  # WinINET registry + crash-safe restore
│     ├─ PingService.cs         # Single-flight per-config latency probe
│     ├─ NetworkTestService.cs  # TCP ping / proxied latency
│     └─ ProxyBypassStore.cs    # User proxy-bypass exceptions
├─ Localization/            # LocalizationManager + EN/FA string tables
└─ Xray/                    # xray.exe + geoip.dat + geosite.dat (bundled)
```

- **Backend:** an [XUI‑Accounter](https://github.com/) API provides authentication, plans, purchases, server configs, events and tickets. The base URL is set in `.env`.
- **Tunnel:** the app spawns the bundled Xray core, opens local SOCKS/HTTP inbounds, verifies traffic passes, then points the Windows system proxy at it.
- **State safety:** on launch the app cleans up anything a crashed previous session left behind (orphaned `xray.exe`, a stale system proxy), and on exit it tears the tunnel down and restores the proxy.

## 🛠️ Tech Stack

| Area | Technology |
|------|-----------|
| UI framework | **WinUI 3** / Windows App SDK 1.x |
| Runtime | **.NET 8** (`net8.0-windows`), self‑contained, unpackaged |
| Controls | **DevWinUI** 9.x |
| VPN core | **Xray‑core 26.x** |
| Backend | XUI‑Accounter REST API |
| Installer | **Inno Setup 6** |
| Languages | English + Persian (RTL) |

## 🚀 Getting Started

### Prerequisites
- Windows 10 (build 17763+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 with the **Windows App SDK / WinUI** workload
- A reachable **XUI‑Accounter** backend

### Configure the backend
Edit the `.env` file next to the project (copied beside the built `.exe`):

```env
API_BASE_URL=https://api.mydomain.com   # your backend base URL
API_TIMEOUT_SECONDS=30                   # request timeout
HEARTBEAT_SECONDS=20                     # backend refresh interval (min 5)
PING_URL=https://www.google.com/generate_204        # per-config ping probe
TUNNEL_CHECK_URL=https://www.google.com/generate_204 # tunnel verification
```

> A per‑user override at `%LOCALAPPDATA%\Lithiumvpn\.env` takes precedence, so you can point a single build at a different backend without recompiling.

### Build & run

```bash
# Debug build (from the repo root)
dotnet build Lithiumvpn/Lithiumvpn/Lithiumvpn.csproj -c Debug -p:Platform=x64

# Run from Visual Studio (F5) or:
dotnet run --project Lithiumvpn/Lithiumvpn/Lithiumvpn.csproj -p:Platform=x64
```

### Publish a self‑contained build

```bash
dotnet publish "Lithiumvpn/Lithiumvpn/Lithiumvpn.csproj" \
  -c Release -r win-x64 -p:Platform=x64 \
  -p:PublishTrimmed=false --self-contained true \
  -o "publish"
```

### Build the installer
Compile `installer.iss` with **Inno Setup 6+** to produce `dist/LithiumVPN-Setup-<version>.exe`:

```bash
ISCC.exe installer.iss
```

The installer is **per‑user** (no admin prompt), creates optional Desktop and startup shortcuts, and cleanly removes per‑user runtime state on uninstall.

## 📦 Runtime data locations

All per‑user state lives under `%LOCALAPPDATA%\Lithiumvpn\`:

| File | Purpose |
|------|---------|
| `session.json` | Access / refresh tokens |
| `language.txt` | Selected UI language |
| `.env` | Optional per‑user backend override |
| `proxy-backup.json` | Crash‑safe system‑proxy restore marker |
| `proxy-bypass.json` | User proxy‑bypass exceptions |
| `xray/` | Working Xray config for the running tunnel |

## 🌐 Localization

Every user‑facing string lives in `Localization/Strings.cs` in both an English (`En`) and Persian (`Fa`) dictionary. XAML binds via `{Binding [Key], Source={StaticResource Loc}}`, and switching language from **Settings** fades the window, flips LTR↔RTL, and re‑renders live. To add a language, extend the string tables and register it in `LocalizationManager`.

## 🤝 Contributing

Contributions are welcome! Please:
1. Fork the repository and create a feature branch.
2. Keep the WinUI/DevWinUI styling consistent with existing pages.
3. Add any new UI string to **both** the `En` and `Fa` dictionaries.
4. Validate generated Xray configs against the shipped core (`xray.exe run -test -c <file>`) when touching connection code.
5. Open a pull request describing your change.

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgements

- [Xray‑core](https://github.com/XTLS/Xray-core) — the VPN engine
- [DevWinUI](https://github.com/ghost1372/DevWinUI) — Fluent WinUI controls
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) — the WinUI 3 platform

---

<div align="center">

Made with ⚡ by [**EXE88**](https://github.com/EXE88)

</div>
