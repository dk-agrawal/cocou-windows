# Coucou Windows

A tiny, privacy-first Windows desktop companion for Claude Code.

## MVP

- Top-edge floating companion UI
- Global cursor-following eyes
- Blink and click/squish reactions
- Local Claude process discovery
- Native Claude Code permission bridge
- Drag-and-drop file surface
- Local settings foundation
- Self-contained Windows build

## Claude Code integration

Coucou uses Claude Code's local hook system. Claude Code exposes a `PermissionRequest` hook that can return an allow/deny decision, so Coucou can show the request in its companion UI and send the user's decision back to Claude Code. citeturn3view0

The repository includes:

- `Coucou.Hook.exe` — tiny stdin-to-local-named-pipe bridge used by Claude Code hooks.
- `ClaudeBridgeServer` — local named-pipe server inside Coucou.
- `docs/claude-code-settings.example.json` — example hook configuration.

### Enable the hooks

1. Build or download the Windows bundle.
2. Put `Coucou.exe` and `Coucou.Hook.exe` in the same install folder.
3. Merge the example hook entries into your user Claude Code settings at `%USERPROFILE%\\.claude\\settings.json`.
4. Start Coucou.
5. Start Claude Code normally.

If Coucou is closed, the hook helper exits without making a decision, so Claude Code keeps its normal permission flow.

## Run from source

Requires Windows 10/11 and .NET 8 SDK.

```powershell
dotnet run --project src/Coucou/Coucou.csproj
```

## Build

```powershell
dotnet publish src/Coucou/Coucou.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/Coucou.Hook/Coucou.Hook.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Privacy

No telemetry, analytics, account sign-in, or required cloud service. The Coucou/Claude bridge uses a local Windows named pipe; it does not expose an internet API.
