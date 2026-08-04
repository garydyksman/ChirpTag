# ChirpTag

A physical capture-the-flag game using ESP32-S3 devices with LoRa radio for combat and key delivery mechanics. Built with .NET nanoFramework.

## Overview

ChirpTag is a multiplayer game where players carry handheld devices that communicate via LoRa radio. Players engage in combat using combat scores, capture keys from enemy flag nodes, and deliver them back to their base for points. The game combines:

- **LoRa radio communication** for low-latency combat and local interactions
- **WiFi/HTTP** for game state synchronization and server coordination
- **E-Paper/OLED displays** for game HUD and status
- **Physical movement** required to reach flag nodes (LoRa range ~1-2km line-of-sight)

## Hardware

### Game Device — Heltec Vision Master E219 (HT-VME213)

- **MCU:** ESP32-S3 (dual-core, 240MHz, 2MB PSRAM)
- **Display:** LCM EN2R13 e-Paper (2.13", 250×122, SPI2)
- **Radio:** SX1262 LoRa (SPI1)
- **Controls:** Two push buttons (attack, cycle targets)
- **Connectivity:** WiFi (for HTTP server sync)

### Flag Node — Heltec WiFi LoRa 32 V4 (HTIT-WB32LAF v4.3)

- **MCU:** ESP32-S3 (dual-core, 240MHz, 2MB PSRAM)
- **Display:** SSD1306 OLED (128×64, I2C)
- **Radio:** SX1262 LoRa
- **Connectivity:** WiFi (for HTTP server sync)
- **Role:** Stationary base station for key capture, delivery, and respawn

## Game Mechanics

### Combat System

- Players have a **combat score** (1-10)
- To attack: select target from HUD, press attack button
- Both devices exchange scores via LoRa
- **Higher score wins** (with wraparound: score 1 beats score 10)
- Loser is eliminated and must respawn

### Capture-the-Flag

1. **Capture:** Reach enemy flag node → press attack → receive 4-byte key
2. **Deliver:** Return to your flag node with enemy key → deliver for points
3. Keys are single-use; once captured, flag node marks key as taken

### Respawn Mechanic

- Dead players must visit the **enemy flag node** to respawn
- Flag node acts as checkpoint (LoRa range verification)
- Server assigns new random combat score upon respawn
- Can be bypassed for testing (`RespawnBypassFlagNodeWait = true`)

### LoRa Communication

All player-to-player and player-to-flag interactions happen via LoRa:
- Combat attack/ack/result packets
- Heartbeat broadcasts (visibility on HUD)
- Key capture and delivery
- Respawn requests

## Project Structure

```
ChirpTag/
├── ChirpTag/                        # Game device firmware (Vision Master E219)
│   ├── Program.cs                   # Hardware init, boot sequence, GPIO ISRs
│   └── ChirpTag.nfproj
│
├── ChirpTagFlagNode/                # Flag node firmware (WiFi LoRa 32 V4)
│   ├── Program.cs                   # OLED init, LoRa setup
│   ├── PixelTextRenderer.cs         # Custom font rendering
│   └── ChirpTagFlagNode.nfproj
│
├── IOD.CaptureTheFlag.NanoFramework/  # Shared game logic library
│   ├── Implementations/
│   │   ├── GamerDevice.cs           # Player device game logic
│   │   ├── GameStateManager.cs      # State tracking, combat list
│   │   ├── MessageHandler.cs        # LoRa packet routing
│   │   ├── PacketBuilder.cs         # LoRa packet construction
│   │   └── PacketParser.cs          # LoRa packet parsing
│   ├── Interfaces/
│   │   ├── IGamerDevice.cs
│   │   ├── IFlagNode.cs             # Flag node contract
│   │   └── IMessageHandler.cs
│   └── Types/
│       └── PacketType.cs            # LoRa packet type constants
│
├── ref/                             # Pre-built DLLs (LoRa, display drivers)
├── .vscode/                         # VS Code launch config, docs
├── CLAUDE.md                        # Architecture and implementation notes
├── flagnode.md                      # Flag node packet flows and design
└── README.md                        # This file
```

## Getting Started

### Prerequisites

1. **.NET nanoFramework** firmware 1.16.0.677 (see [Firmware Notes](#firmware-notes))
2. **VS Code** with nanoFramework extension (v1.0.247+)
3. **nanoff** CLI tool: `dotnet tool install -g nanoff`
4. **macOS or Windows** (extension works on both)

### Firmware Notes

**Use firmware version 1.16.0.677** (not the latest). Version 1.17.0+ introduced breaking SPI bus changes that cause `System.ArgumentException` on SPI device initialization. See `.vscode/firmware-package-versions.md` for details.

#### Flash Firmware (macOS/Linux)

```bash
# Find device
nanoff --listports

# Flash firmware (hold BOOT button when prompted)
nanoff --target ESP32_S3 --serialport /dev/cu.usbmodem11301 --update --fwversion 1.16.0.677 --masserase
```

**Note:** On ESP32-S3, the serial port changes after flashing:
- **Bootloader mode:** `/dev/cu.usbmodem11301`
- **nanoFramework running:** `/dev/cu.usbmodem1234561`

Update `.vscode/launch.json` with the nanoFramework port.

### Build and Deploy

#### VS Code (Recommended)

1. Open workspace in VS Code
2. Select project (`ChirpTag` or `ChirpTagFlagNode`)
3. Press **F5** to build and deploy
4. Debugger will attach automatically

#### Manual Deploy

```bash
# Build
msbuild ChirpTag/ChirpTag.nfproj -p:Configuration=Debug \
  -p:NanoFrameworkProjectSystemPath=$HOME/.vscode/extensions/nanoframework.vscode-nanoframework-*/dist/utils/nanoFramework/v1.0/

# Deploy
nanoff --nanodevice --serialport /dev/cu.usbmodem1234561 --deploy \
  --image ChirpTag/bin/Debug/ChirpTag.pe
```

### Configuration

Device settings use a two-layer merge:

1. **`AppSettingsBuiltIn.cs`** — compiled defaults
2. **`I:\appsettings.json`** — runtime overrides (on device SPIFFS)

Example `appsettings.json`:

```json
{
  "playerName": "Player1",
  "wifiSsid": "YourWiFi",
  "wifiPassword": "password",
  "gameApiBaseUrl": "http://192.168.1.100:5000",
  "gameApiSslNoVerify": true,
  "allowHudWithoutValidDeviceId": false,
  "ignoreAttackRangeLimit": false
}
```

**Deploy config to device:**

```bash
# Create config file on device SPIFFS
# (requires nanoFramework.System.IO.FileSystem)
```

See `appsettings.sample.json` for reference.

## Development

### macOS Setup

1. **Install nanoff:**
   ```bash
   dotnet tool install -g nanoff
   ```

2. **Add to PATH** (if needed):
   ```bash
   echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zprofile
   source ~/.zprofile
   ```

3. **GitHub auth** (for pushing):
   ```bash
   brew install gh
   gh auth login
   gh auth setup-git
   ```

4. **Git identity:**
   ```bash
   git config --global user.name "Your Name"
   git config --global user.email "your@email.com"
   ```

### Serial Monitoring

View device debug output:

```bash
# Monitor serial output (Ctrl+A, K to exit screen)
screen /dev/cu.usbmodem1234561 115200

# Or use cat with timeout
cat /dev/cu.usbmodem1234561
```

### Common Issues

**"Device not found" after deploy:**
- Check serial port changed after firmware flash
- Update `.vscode/launch.json` device field
- Run `nanoff --listports` to find current port

**Display not working:**
- Verify I2C/SPI pin mappings match hardware
- Check `Configuration.SetPinFunction()` calls
- Add `Console.WriteLine()` debug output to trace init sequence

**WiFi and LoRa interference:**
- WiFi and LoRa cannot run simultaneously on ESP32-S3
- Always tear down WiFi before resuming LoRa
- Use `WifiHttpBridge.EnableForHttp()` / `TearDownRadio()`

**Memory issues:**
- Pre-allocate arrays in constructors, not in loops
- Avoid `new` in LoRa RX callbacks
- E-Paper framebuffer is ~4KB; OLED is ~1KB

## Documentation

- **[CLAUDE.md](CLAUDE.md)** — Architecture, threading model, boot sequence, packet format
- **[flagnode.md](flagnode.md)** — Flag node design, packet flows, implementation checklist
- **[.vscode/firmware-package-versions.md](.vscode/firmware-package-versions.md)** — Firmware and NuGet package compatibility

## LoRa Packet Types

| Hex  | Name | Direction | Payload | Purpose |
|------|------|-----------|---------|---------|
| 0x01 | Heartbeat | Broadcast | 1 byte (alive) | Player visibility |
| 0x02 | Attack | P→P | Empty | Initiate combat |
| 0x03 | AttackAck | P→P | 1 byte (score) | Respond with combat score |
| 0x04 | FlagTransfer | P→P | 4 bytes (key) | Transfer captured key |
| 0x05 | Capture | P→F | Empty | Request key from flag |
| 0x06 | KeyGrant | F→P | 4 bytes (key) | Grant key to player |
| 0x07 | Deliver | P→F | 4 bytes (key) | Deliver key for scoring |
| 0x08 | RespawnReq | P→F | Empty | Request respawn auth |
| 0x09 | RespawnAck | F→P | 1 byte (score) | Grant respawn |
| 0x0A | GameStart | Broadcast | Empty | Game started |
| 0x0B | GameEnd | Broadcast | 1 byte (winnerId) | Game ended |
| 0x0C | CombatResult | P→P | 1 byte (winnerId) | Combat result notification |

**P→P:** Player to Player  
**P→F:** Player to Flag Node  
**F→P:** Flag Node to Player

Wire format: `[ fromDeviceId | msgType | targetId | ...data | crc ]`

## Server Integration

The game requires an HTTP server for:
- Game state management (starting/stopping games)
- Player roster and scoring
- Key generation and validation
- Respawn score assignment

### Expected Endpoints

- `GET /api/game` — Current game state
- `POST /api/game/register` — Register player
- `GET /api/game/respawn/{deviceId}` — Fetch respawn score
- `POST /api/game/deliver` — Report key delivery

*(Server implementation not included in this repository)*

## Architecture Highlights

### Boot Sequence

1. **Phase 1:** Init display, connect WiFi, poll server for game
2. **Phase 2:** Register player, wait for game to go Active
3. **Phase 3:** Tear down WiFi, init LoRa, start game loops

### Threading Model (GamerDevice)

- **Main thread:** GPIO polling (25ms), dispatches to GamerDevice
- **HeartbeatLoop:** Broadcast LoRa heartbeat every 5s
- **TxLoop:** Drain LoRa TX queue every 500ms
- **UiLoop:** Refresh e-Paper display every 1s (with diff optimization)

Cross-thread signaling uses `lock` + `bool` fields (no `volatile` in nanoFramework).

### Memory Management

- Hot-path arrays pre-allocated at construction (`MaxPlayers = 16`, `MaxDeviceId = 256`)
- No `new` in LoRa callbacks
- E-Paper framebuffer allocated first (before WiFi) to claim heap
- OLED uses ~75% less RAM than e-Paper

### Combat Resolution

```csharp
bool ResolveCombat(byte myScore, byte theirScore)
{
    if (myScore == theirScore) return false; // tie = attacker loses
    
    // Wraparound rule: 1 beats 10
    if (myScore == 1 && theirScore == 10) return true;
    if (myScore == 10 && theirScore == 1) return false;
    
    return myScore > theirScore;
}
```

## Testing

### Standalone Tests

**Game Device:**
1. Display init → show boot splash
2. WiFi connect → fetch game state
3. LoRa init → broadcast heartbeat
4. Button press → log to serial

**Flag Node:**
1. OLED init → show "FLAG NODE" text
2. WiFi connect → fetch flag key
3. LoRa init → listen for packets
4. Send test packet → verify radio working

### Integration Tests

1. Two game devices → attack each other → verify combat resolution
2. Game device + flag node → capture key → verify KeyGrant received
3. Game device + flag node → deliver key → verify server POST
4. Dead player + flag node → respawn → verify RespawnAck received

### Debug Output

Enable verbose logging via conditional compilation:
- `GAMER_DEVICE_TRACE` — GamerDevice logs
- `VERBOSE_GAMESTATE` — GameStateManager logs
- `CombatListDiagnostics.Enabled = true` — Combat list logs

## License

*(Add your license here)*

## Credits

Built with [.NET nanoFramework](https://github.com/nanoframework)

Hardware by [Heltec Automation](https://heltec.org/)
