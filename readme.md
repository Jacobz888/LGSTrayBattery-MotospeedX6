# Motospeed X6 Tray Battery Monitor

A lightweight Windows system tray battery monitor and status indicator specifically tailored for the **Motospeed X6 Wireless Gaming Mouse**. Displays live battery percentage, charging state, and rich tooltips directly in your Windows taskbar with minimal resource usage and zero bloat.

*(Forked and adapted from [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) with dedicated Motospeed X6 HID support).*

---

## ⚡ Quick Start & Download

[![GitHub Release](https://img.shields.io/github/v/release/Jacobz888/LGSTrayBattery-MotospeedX6?sort=semver)](https://github.com/Jacobz888/LGSTrayBattery-MotospeedX6/releases/latest)

Download the latest version from the [Releases Page](https://github.com/Jacobz888/LGSTrayBattery-MotospeedX6/releases/latest):

- **`Release_v1_0_0-standalone.zip` (Recommended)**: Self-contained build bundled with the .NET 8 runtime. Just extract the archive and run `LGSTray.exe` — no prerequisites needed!
- **`Release_v1_0_0.zip`**: Framework-dependent build (~3 MB) for systems that already have [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) installed.

---

## 🔍 How It Works for Motospeed X6

Instead of requiring heavy proprietary software or clunky OEM background utilities, this application communicates directly with your **Motospeed X6** over native USB HID:

### 1. Direct USB HID Communication
The app communicates with the device through `hidapi.dll` without any external drivers needed:
- **Vendor ID (VID)**: `0x0BDA`
- **Product IDs (PID)**:
  - `0xFFE0` / `0xFFF2`: 2.4 GHz wireless receiver / dongle mode
  - `0xFFF1`: Direct USB-C wired mode (plugged in & charging)
- **Target Interface**: The application attaches to **Interface 4 (`mi_04`)**, which is the mouse's vendor-specific telemetry report channel.

### 2. Battery & Charging Protocol
- **Telemetry Request**: Every poll interval, the app writes an HID request report (`0xB5 0x06`) to the mouse.
- **Telemetry Response**: The mouse replies with a 64-byte report starting with report ID `0xB4`.
- **Level Calculation & State Detection**:
  - The raw battery byte is extracted from byte index 20 (`resp[20]`).
  - **Charging over Wireless**: If the raw value is $\ge 128$, the mouse is actively charging (e.g. on a dock or cable). The actual percentage is calculated as `raw_value - 128` (clamped between 0% and 100%).
  - **Discharging**: If the raw value is $< 128$, the mouse is operating on battery power, and the value directly represents the percentage (0–100%).
  - **Wired Connection**: When connected directly via USB cable (PID `0xFFF1`), the app recognizes the plugged-in charging state immediately.

---

## ✨ Features

- **Real-Time Tray Icon**: Clean icon right in the Windows notification area showing battery level at a glance.
- **Numerical Mode**: Option to display the battery percentage directly as a number on the tray icon.
- **Rich Hover Tooltips**: Hover over the tray icon to see the device name, battery percentage, and charging / discharging status.
- **Theme Reactive**: Automatically switches between Light and Dark mode icon designs to match your Windows theme in real time.
- **Local HTTP REST API**: Optional embedded web server (running on `http://localhost:12321/`) exposing device battery data for widgets, Stream Deck profiles, or custom integrations.
- **Minimal Resource Footprint**: Extremely light on RAM and CPU, polling only on configurable intervals to protect mouse sleep mode and battery life.

---

## ⚙️ Configuration (`appsettings.toml`)

Configuration is stored in `appsettings.toml` in the app directory:

```toml
[UI]
enableRichToolTips = true

[Motospeed]
enabled = true          # Set to true to enable Motospeed X6 polling
pollPeriod = 60         # Polling interval in seconds (default: 60)

[HTTPServer]
enabled = true          # Set to false to disable local HTTP server
addr = "localhost"      # Bind address
port = 12321            # Port number
useIpv6 = true

[GHub]
enabled = false         # Disable Logitech G Hub polling if not needed

[Native]
enabled = false         # Disable Logitech HID++ polling if not needed
```

> **Tip**: If you only use the Motospeed X6 mouse, set `[GHub].enabled = false` and `[Native].enabled = false` to disable scanning for Logitech devices and ensure the fastest, leanest startup.

---

## 🛠️ How to Build from Source

### Prerequisites
- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Python 3.x (to run the packaging helper `publish.py`)

### Building
1. Clone the repository:
   ```powershell
   git clone https://github.com/Jacobz888/LGSTrayBattery-MotospeedX6.git
   cd LGSTrayBattery-MotospeedX6
   ```

2. Build the project:
   ```powershell
   dotnet build
   ```

3. Package release archives:
   ```powershell
   python publish.py
   ```
   Packaged zip files will be placed in `bin/Release/Publish/`.

---

## 💡 Acknowledgements

- [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) — The original tray application base and WPF notification framework.
- [libusb/hidapi](https://github.com/libusb/hidapi) — Library for communicating with USB HID devices.
