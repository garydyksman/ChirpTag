# Configuration

## How config works

Device configuration lives in `LocalConfig.cs` (one file per project under `ChirpTag/` and `ChirpTagFlagNode/`). These files are **gitignored** — each developer creates their own from the template before first build.

```
ChirpTag/LocalConfig.template.cs   → copy to ChirpTag/LocalConfig.cs
ChirpTagFlagNode/LocalConfig.template.cs → copy to ChirpTagFlagNode/LocalConfig.cs
```

Edit the copy to set your WiFi credentials, dev tunnel URL, player name / flag node ID, etc. The firmware reads exclusively from these compile-time constants — there is no runtime SPIFFS config loading.

> **Note:** `AppSettingsBuiltIn.cs` exists in the repo but is intentionally empty. Do not rely on it for runtime config.

## WriteConfig.cs

`WriteConfig.cs` (repo root) is a one-time utility snippet for writing `I:\appsettings.json` to device SPIFFS. **The current firmware does not read this file at runtime** — it exists as a reference for future SPIFFS-based config loading. Do not follow its instructions expecting runtime config to be applied.

## When config is lost

Config is compiled into the firmware, so it persists as long as your build does. A `nanoff --masserase` wipes SPIFFS but does not affect compiled-in constants.

## Changing config mid-game

Edit `LocalConfig.cs`, rebuild, and redeploy via the nanoFramework Visual Studio extension.
