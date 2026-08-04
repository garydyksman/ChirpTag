# Flag Node Design

The flag node acts as a physical checkpoint/gatekeeper in the capture-the-flag game. It's a stationary device placed at a team's base that players must interact with via LoRa radio.

## Hardware

**Heltec WiFi LoRa 32 V4 (HTIT-WB32LAF v4.3)**
- SSD1306 OLED display (I2C, 128×64)
- SX1262 LoRa radio
- WiFi for HTTP server communication
- ESP32-S3 running .NET nanoFramework 1.16.0.677

## Purpose

The flag node serves three main functions:
1. **Capture** — Grant keys to players who reach the enemy base
2. **Deliver** — Accept captured keys and report victory to server
3. **Respawn** — Act as a respawn checkpoint requiring physical presence (LoRa range)

## LoRa Packet Interactions

### 1. Capture Flow (Get Key)

**Scenario:** Player reaches enemy flag node and presses the attack button to capture the flag.

```
Player                Flag Node              Server
  |                       |                     |
  |--[Capture]----------->|                     |
  |   targetId=flagNodeId |                     |
  |                       |                     |
  |<--[KeyGrant]----------|                     |
  |   data=[k0,k1,k2,k3]  |                     |
  |                       |                     |
```

**Packets:**
- **`Capture`** (0x05) — Empty payload, player requesting the key
- **`KeyGrant`** (0x06) — 4-byte key `[k0, k1, k2, k3]` sent back to player

**Flag Node Actions:**
1. Receive `Capture` packet from player
2. Check if key has already been taken (`KeyTaken` flag)
3. If available, send `KeyGrant` with the 4-byte key
4. Mark key as taken (set `KeyTaken = true`)
5. Update display: "KEY CAPTURED"

**Key Source:** The flag node fetches its key from the HTTP server at game start, not generated locally.

---

### 2. Deliver Flow (Return Key)

**Scenario:** Player with captured enemy key returns to their own flag node to score.

```
Player                Flag Node              Server
  |                       |                     |
  |--[Deliver]----------->|                     |
  |   data=[k0,k1,k2,k3]  |                     |
  |                       |                     |
  |                       |--[HTTP POST]------->|
  |                       |   /deliver          |
  |                       |   {deviceId, key}   |
  |                       |<-[200 OK]-----------| (or 400 if wrong key)
  |                       |                     |
```

**Packets:**
- **`Deliver`** (0x07) — 4-byte key `[k0, k1, k2, k3]` player is attempting to deliver

**Flag Node Actions:**
1. Receive `Deliver` packet with key from player
2. **Pause LoRa, enable WiFi** (WiFi and LoRa cannot run simultaneously)
3. Call `ReportDeliver(deviceId, key)` via HTTP to server
4. Server validates key and awards points if correct
5. **Tear down WiFi, resume LoRa**
6. Update display with result

**No LoRa response packet** — the player doesn't get immediate feedback via LoRa. The server updates game state, and players see the result via their next HTTP poll or game state broadcast.

---

### 3. Respawn Flow (Checkpoint)

**Scenario:** Dead player presses respawn button. Game requires them to be in LoRa range of enemy flag node.

```
Player                Flag Node              Server
  |                       |                     |
  |--[WiFi HTTP GET]------|-------------------->| (player fetches respawn score via WiFi)
  |                       |                     |
  |--[RespawnReq]-------->|                     |
  |   targetId=flagNodeId |                     |
  |                       |                     |
  |                       |--[WiFi HTTP GET]--->| (flag node fetches score on behalf of player)
  |                       |<-[newScore]---------| 
  |                       |                     |
  |<--[RespawnAck]--------|                     |
  |   data=[newScore]     |                     |
  |                       |                     |
```

**Packets:**
- **`RespawnReq`** (0x08) — Empty payload, player requesting respawn authorization
- **`RespawnAck`** (0x09) — 1-byte `newCombatNumber` to assign to respawning player

**Flag Node Actions:**
1. Receive `RespawnReq` packet from dead player
2. **Pause LoRa, enable WiFi**
3. Call `FetchRespawnNumber(deviceId)` via HTTP to get new combat score for player
4. **Tear down WiFi, resume LoRa**
5. Send `RespawnAck` with new combat score back to player
6. Update display: "RESPAWN: DeviceID"

**Why This Flow?**
- **Range check** — player must be physically near the enemy flag (within LoRa range)
- **Server coordination** — flag node acts as intermediary to fetch official respawn score
- **Gameplay mechanic** — creates capture-the-flag dynamic where death requires visiting enemy base

**Bypass Mode:** The game device has a `RespawnBypassFlagNodeWait` flag that, when `true`, skips the LoRa `RespawnReq`/`RespawnAck` flow and applies the HTTP-fetched score immediately. This is useful for:
- Solo testing without a flag node
- Debugging
- Alternate game modes

---

## Packet Type Reference

| Type | Hex | Name | Direction | Payload | Purpose |
|------|-----|------|-----------|---------|---------|
| 0x05 | `Capture` | Player → Flag | Empty | Request key from flag |
| 0x06 | `KeyGrant` | Flag → Player | 4 bytes (key) | Grant captured key |
| 0x07 | `Deliver` | Player → Flag | 4 bytes (key) | Deliver captured key for scoring |
| 0x08 | `RespawnReq` | Player → Flag | Empty | Request respawn authorization |
| 0x09 | `RespawnAck` | Flag → Player | 1 byte (score) | Grant respawn with new combat score |

---

## LoRa Packet Wire Format

All packets follow this structure:

```
[ fromDeviceId (1) | msgType (1) | targetId (1) | ...data | crc (1) ]
```

- **fromDeviceId** — sender's device ID
- **msgType** — packet type constant (see table above)
- **targetId** — recipient device ID (`0x00` = broadcast)
- **data** — payload (empty, 1 byte, or 4 bytes depending on packet type)
- **crc** — 8-bit CRC checksum

`MessageHandler` validates exact length (per `PacketParser.ExpectedLength`) and CRC before routing.

---

## Implementation Checklist

- [ ] Implement `IFlagNode` interface
- [ ] Initialize SSD1306 OLED display (I2C pins: SDA=17, SCL=18, RST=21)
- [ ] Initialize SX1262 LoRa radio (SPI1 bus)
- [ ] Fetch flag key from HTTP server at game start
- [ ] Handle `OnCaptureReceived()` → send `KeyGrant`
- [ ] Handle `OnDeliverReceived()` → HTTP POST to server
- [ ] Handle `OnRespawnRequestReceived()` → HTTP GET + send `RespawnAck`
- [ ] WiFi/LoRa coordination (pause LoRa during WiFi, resume after)
- [ ] Display updates for each interaction
- [ ] Message queuing and threading (follow `GamerDevice` pattern)

---

## Threading Model

The flag node should follow a similar pattern to `GamerDevice`:

1. **Main thread** — handles LoRa RX callbacks, dispatches to handler methods
2. **TxLoop thread** — drains outgoing packet queue to LoRa every 500ms
3. **UiLoop thread** (optional) — updates display periodically

**WiFi/LoRa Coordination:**
- WiFi and LoRa cannot be active simultaneously on ESP32-S3
- Before HTTP calls: pause LoRa, enable WiFi via `WifiHttpBridge`
- After HTTP calls: tear down WiFi, resume LoRa
- Use callbacks or mutexes to coordinate state

---

## Memory Considerations

- Pre-allocate packet buffers (avoid `new` in LoRa callbacks)
- OLED framebuffer is ~1KB (128×64 ÷ 8) — much lighter than e-Paper
- Keep HTTP response buffers small
- Reuse objects where possible

---

## Testing

**Standalone Testing:**
- Display "Hello World" pattern → verify OLED working
- Send test LoRa packets → verify radio working
- HTTP GET test endpoint → verify WiFi + HTTP working

**Integration Testing:**
- Game device sends `Capture` → flag node responds with `KeyGrant`
- Game device sends `Deliver` with wrong key → server rejects
- Game device sends `RespawnReq` → flag node fetches score + responds `RespawnAck`
- Monitor serial output for timing/coordination issues
