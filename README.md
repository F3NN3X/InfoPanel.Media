# InfoPanel.Media

A universal media tracking plugin for [InfoPanel](https://github.com/habibrehmansg/infopanel) that displays real-time playback data from **any media source** — Spotify, browsers, VLC, Windows Media Player, and more.

Built on the Windows [Global System Media Transport Controls](https://learn.microsoft.com/en-us/uwp/api/windows.media.control) (GSMTC) API. No API keys, no OAuth, no authentication required.

---

## Features

- **Universal media tracking** — works with any app that integrates with Windows media transport controls
- **Zero configuration** — no API keys or authentication needed, just install and go
- **Real-time updates** — 1Hz refresh with progress interpolation between OS events
- **Album art over HTTP** — serves cover art via a built-in HTTP server for use with InfoPanel's HTTP image element
- **Source detection** — identifies which app is playing (Spotify, Chrome, VLC, etc.)
- **Customizable messages** — configure display text for idle/paused states via INI

## Supported Sources

Any application that integrates with Windows media transport controls, including:

| Source | Status |
|--------|--------|
| Spotify (desktop) | Fully supported |
| Chrome / Edge / Firefox | Supported (YouTube, SoundCloud, etc.) |
| VLC Media Player | Fully supported |
| Windows Media Player | Fully supported |
| Groove Music | Fully supported |
| foobar2000 | Fully supported |
| MPC-HC | Fully supported |
| AIMP | Fully supported |
| Any GSMTC-compatible app | Supported |

## Plugin Outputs

### Text Entries
| ID | Description | Example |
|----|-------------|---------|
| `current-track` | Current track name | `Bohemian Rhapsody` |
| `artist` | Artist name | `Queen` |
| `album` | Album name | `A Night at the Opera` |
| `elapsed-time` | Elapsed playback time | `02:15` |
| `remaining-time` | Remaining playback time | `03:40` |
| `cover-art` | Album art HTTP URL | `http://localhost:52312/cover` |
| `source-app` | Active media source | `Spotify` |

### Sensor Entries
| ID | Description | Values |
|----|-------------|--------|
| `track-progress` | Track progress percentage | `0.0` – `100.0` |
| `session-state` | Media session state | `0` = No Session, `1` = Active, `2` = Error |
| `playback-state` | Playback state | `0` = Not Playing, `1` = Paused, `2` = Playing |

## Installation

1. Download the latest release from [Releases](https://github.com/F3NN3X/InfoPanel.Media/releases)
2. Extract the `InfoPanel.Media` folder to your InfoPanel plugins directory (typically `C:\ProgramData\InfoPanel\plugins\`)
3. Restart InfoPanel
4. The plugin will automatically detect any active media session

## Configuration

The plugin creates `InfoPanel.Media.dll.ini` alongside the DLL on first run:

```ini
[Media Plugin]
MaxDisplayLength=20
NoTrackMessage=No music playing
PausedMessage=
NoTrackArtistMessage=-
PrioritySources=Spotify,Apple Music,VLC,foobar2000,AIMP,Groove Music,Windows Media Player,MPC-HC
CoverArtPort=52312
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxDisplayLength` | `20` | Maximum characters for track/artist/album before truncation |
| `NoTrackMessage` | `No music playing` | Message displayed when no media is active |
| `PausedMessage` | *(empty)* | Message when paused. Empty = keep track info visible |
| `NoTrackArtistMessage` | `-` | Artist field text when no track or using custom paused message |
| `PrioritySources` | `Spotify,Apple Music,...` | Comma-separated list of apps to prioritize (see below) |
| `CoverArtPort` | `52312` | HTTP port for serving cover art. Set to `0` to disable (falls back to file path) |

### Session Prioritization

When multiple apps have active media sessions (e.g. Spotify and a YouTube tab), the plugin picks which one to display based on the `PrioritySources` list. Apps earlier in the list take priority — a paused Spotify will still be shown over a playing browser tab.

The default list prioritizes dedicated music players over browsers:

```
Spotify,Apple Music,VLC,foobar2000,AIMP,Groove Music,Windows Media Player,MPC-HC
```

To customize, edit the comma-separated list in `InfoPanel.Media.dll.ini`. Use the **friendly app names** shown in the table below:

| Friendly Name | Raw App ID(s) |
|---------------|---------------|
| Spotify | `Spotify.exe` |
| Chrome | `chrome.exe` |
| Edge | `msedge.exe` |
| Firefox | `firefox.exe` |
| VLC | `vlc.exe` |
| Windows Media Player | `wmplayer.exe` |
| Groove Music | `Music.UI` |
| foobar2000 | `foobar2000.exe` |
| AIMP | `AIMP.exe` |
| MPC-HC | `mpc-hc.exe`, `mpc-hc64.exe` |
| Apple Music | *(UWP)* |

Apps not in the list are treated as lowest priority. If `PrioritySources` is empty, the plugin falls back to the OS default (most recently active session).

## Building from Source

Requires [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows x64).

```bash
# Debug build
dotnet build -c Debug

# Release build (outputs to bin/Release/.../InfoPanel.Media-v{VERSION}/InfoPanel.Media/)
dotnet build -c Release
```

## Architecture

The plugin follows an event-driven architecture with a single service:

```
MediaPlugin (BasePlugin)
    ├── MediaPlaybackService
    │       ├── GSMTC SessionManager (OS-level media tracking)
    │       ├── Session events (MediaProperties, PlaybackInfo, Timeline)
    │       ├── Progress estimation (1Hz interpolation)
    │       └── Thumbnail extraction (SHA256 change detection)
    └── CoverArtServer
            └── HttpListener (serves album art over HTTP)
```

- **`MediaPlugin`** — main plugin class, manages lifecycle and UI elements
- **`MediaPlaybackService`** — interfaces with the Windows GSMTC API, handles session tracking, media property changes, timeline sync, and album art extraction
- **`CoverArtServer`** — lightweight HTTP server that serves album art at `http://localhost:{port}/cover` for InfoPanel's HTTP image element

## Requirements

- Windows 10 version 1903 (build 19041) or later
- .NET 8.0 Runtime
- [InfoPanel](https://github.com/habibrehmansg/infopanel)

## License

MIT

## Credits

- [InfoPanel](https://github.com/habibrehmansg/infopanel) by habibrehmansg
- [ini-parser](https://github.com/rickyah/ini-parser) for INI configuration
