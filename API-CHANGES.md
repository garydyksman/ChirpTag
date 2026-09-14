# API Changes Summary

## Server API Changes Implemented

The firmware has been updated to match the new game server API (swagger v1).

### 1. PlayerInfo Type Field

**Added `Type` field** to distinguish between player and flag node devices.

- `PlayerInfo.Type` (string) — e.g. "Player" or "FlagNode"
- Updated `PlayerInfoDto` in `GameApiHttpClient` to parse this field
- Server returns this in `/api/game` responses

### 2. MAC-Based Registration

**New registration endpoints** that use device MAC address instead of just player name:

#### `/api/game/register/player` (POST)
Register a player device by MAC address with optional player name.

```csharp
// Interface method
PlayerSetup RegisterPlayer(string macAddress, string playerName);

// Example usage
string mac = WiFiBootstrap.GetMacAddress();  // "AA:BB:CC:DD:EE:FF"
PlayerSetup setup = httpClient.RegisterPlayer(mac, "Player1");
byte deviceId = setup.DeviceId;
```

#### `/api/game/register/flagnode` (POST)
Register a flag node device by MAC address.

```csharp
// Interface method
PlayerSetup RegisterFlagNode(string macAddress);

// Example usage  
string mac = WiFiHelper.GetMacAddress();
PlayerSetup setup = httpClient.RegisterFlagNode(mac);
byte deviceId = setup.DeviceId;
```

#### `/api/game/register` (POST) — LEGACY
The original registration endpoint still exists for backward compatibility.

```csharp
PlayerSetup Register(string playerName);  // Still works
```

### 3. MAC Address Helpers

Added helper methods to retrieve WiFi MAC address:

**Game device (`ChirpTag`):**
```csharp
string mac = WiFiBootstrap.GetMacAddress();
```

**Flag node (`ChirpTagFlagNode`):**
```csharp
string mac = WiFiHelper.GetMacAddress();
```

Returns MAC address as colon-separated hex string (e.g. "AA:BB:CC:DD:EE:FF").

## Migration Path

### Current Code (using legacy endpoint)
```csharp
PlayerSetup setup = httpClient.Register(playerName);
```

### New Code (using MAC-based endpoint)
```csharp
string mac = WiFiBootstrap.GetMacAddress();
PlayerSetup setup = httpClient.RegisterPlayer(mac, playerName);
```

## Files Changed

1. **IOD.CaptureTheFlag.NanoFramework/Types/PlayerInfo.cs**
   - Added `Type` property (string)

2. **IOD.CaptureTheFlag.NanoFramework/Interfaces/IGameHttpClient.cs**
   - Added `RegisterPlayer(macAddress, playerName)` method
   - Added `RegisterFlagNode(macAddress)` method
   - Marked `Register(playerName)` as LEGACY

3. **IOD.CaptureTheFlag.NanoFramework/Implementations/GameApiHttpClient.cs**
   - Updated `PlayerInfoDto` with `Type` field
   - Updated `GetCurrentGame()` to parse `Type` field
   - Implemented `RegisterPlayer()` method
   - Implemented `RegisterFlagNode()` method

4. **ChirpTag/WiFiBootstrap.cs**
   - Added `GetMacAddress()` static method
   - Added private `ByteToHex()` helper

5. **ChirpTagFlagNode/WiFiHelper.cs**
   - Added `GetMacAddress()` static method
   - Added private `ByteToHex()` helper

## Next Steps

To use the new registration flow:

1. **Update `ChirpTag/Program.cs`** to call `RegisterPlayer` with MAC address
2. **Update `ChirpTagFlagNode/Program.cs`** to call `RegisterFlagNode` with MAC address
3. Test both registration flows with the updated server
4. Consider removing the legacy `Register()` method once all devices are updated

## Testing

Before deploying:

1. Verify MAC address format matches server expectations (colon-separated hex)
2. Test player registration with and without player name
3. Test flag node registration
4. Verify server correctly assigns device IDs based on MAC address
5. Test reconnection scenario (MAC already registered)
