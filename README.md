# HWmonitor

A lightweight Windows hardware monitor with a clean, macOS‑style interface, built with **C# / .NET 10** and **Avalonia 12**.

## Download

Everything you need is in the [`Release`](Release) folder:

| File | What it is |
|---|---|
| `Release/HWmonitor-Setup.exe` | Installer: installs to Program Files, adds Start‑menu/desktop shortcuts, and registers in **Settings → Apps** with an uninstaller. |
| `Release/HWmonitor.exe` | Portable single‑file app. No install needed, no .NET runtime required. |

**Uninstall:** use *Settings → Apps → HWmonitor → Uninstall*, run `Uninstall.exe` in the install folder, or run the setup again and click **Uninstall**. You can choose whether to also delete your settings and session history.

> HWmonitor asks for administrator rights. It needs them to read CPU/disk sensors and to capture frame rates through Windows ETW.

## Features

**Hardware dashboard**
- CPU model, total load as a speedometer gauge, clock, package power, temperature bar (0–100 °C)
- One gauge for every CPU core, with that core's clock and temperature
- Every GPU: model, load, VRAM usage gauge, core and memory clocks, power, fan, temperature and hot spot
- RAM: modules, type, speed, used/total, usage gauge
- Every SSD/HDD: model, capacity, used space, live activity, read/write speeds, temperature
- **Graphics driver check:** shows the installed driver version and date, and flags when an update is likely available

**Network dashboard**: on‑demand speed test (ping, jitter, download, upload) with a live speedometer and a history of past tests. It uses Cloudflare's public speed test servers.

**Gaming dashboard**
- Detects games on its own: Steam, Epic, GOG, Xbox, Riot, EA, Ubisoft and Battle.net installs, plus any full‑screen app that is rendering
- Live FPS gauge, frame time, frame‑time graph, 1 % low, CPU/GPU temperatures
- Session history with play time, average FPS, 1 % and 0.1 % lows, average and worst frame time, peak CPU/GPU temperature, peak VRAM and average loads
- "Most played" overview with total time per game

**Customisable**: dark or light theme, accent colour, font family and size, window opacity, the colour of every gauge and temperature bar, sensor update interval, start with Windows, and process allow/ignore lists for game detection.

## Light on resources

- Sensors are read on one low‑priority background thread (1 s by default, adjustable). Polling slows down and the UI stops updating while the window is minimized.
- Gauges are drawn directly with a custom renderer. There is no charting library and no per‑frame animation.
- Frame capture enables only the DXGI/D3D9 `Present` ETW events, the same source PresentMon uses.
- Frame statistics use a fixed‑size histogram, so memory use stays the same no matter how long you play.

## Notes and limitations

- **CPU temperature:** recent LibreHardwareMonitor versions read CPU temperatures through the free [PawnIO](https://pawnio.eu) driver. If the CPU temperature shows "—", install PawnIO and restart HWmonitor.
- **FPS capture** covers DirectX 9–12 games and Vulkan/OpenGL games that present through DXGI. Pure Vulkan/OpenGL presentation is not captured.
- **Driver updates** are flagged based on driver age (more than 60 days). NVIDIA, AMD and Intel don't offer a stable public "latest version" API, so the badge links to the vendor's download page.

## Build

```powershell
.\build.ps1   # publishes the app and produces Release\HWmonitor.exe and Release\HWmonitor-Setup.exe
```

Requirements: .NET 10 SDK. The installer stub targets .NET Framework 4.8, which is built into Windows 10 and 11.

Data (settings, sessions, speed tests) is stored in `%AppData%\HWmonitor`.
