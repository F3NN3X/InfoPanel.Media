# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.1] - 2026-03-09

### Fixed

- Improved source app resolution to handle path-based and UWP-style `SourceAppUserModelId` values (fixes detection of apps reporting full file paths)

### Added

- Diagnostic logging of all available GSMTC sessions during session selection
- VLC setup instructions in README (requires [vlc-win10smtc](https://github.com/spmn/vlc-win10smtc) plugin)

## [1.3.0] - 2026-03-02

### Added

- Status images: configurable play/pause/idle indicator icons overlaid on cover art, replacing it, or served separately
- Four display modes via `StatusImageMode` setting: `Off`, `Replace`, `Overlay` (default), `Separate`
- Default 128x128 white-on-transparent status icons bundled with the plugin (`play.png`, `pause.png`, `idle.png`)
- `StatusImageStates` setting: choose which playback states show a status icon on cover art (e.g. `Pause,Idle` to hide play icon from overlay/replace while the `/status` endpoint still serves all states)
- `OverlayScale` setting: control overlay icon size as a percentage (1-100) of cover art's smaller dimension
- `PlayImage`, `PauseImage`, `IdleImage` settings: use custom icon files (relative or absolute paths)
- New `status-image` plugin text element: serves the current status icon via HTTP at `/status` endpoint
- `/status` HTTP endpoint on the cover art server for serving status icons independently of cover art

### Changed

- `System.Drawing.Common` NuGet dependency added for image compositing
- Cover art server now handles both `/cover` and `/status` endpoints
- `status-image` element is always registered in the plugin UI (populated only when mode is not `Off`)
- Cover art and status images now served from in-memory buffers for faster HTTP responses and reduced flicker on state/track changes

## [1.2.1] - 2026-03-02

### Fixed

- Cover art image no longer blinks on every update; URL cache-buster now uses a version counter that only changes on actual track/art changes

## [1.2.0] - 2026-03-02

### Added

- Cover art HTTP server: serves album art over HTTP (`http://localhost:52312/cover`) for use with InfoPanel's HTTP image element
- `CoverArtPort` INI setting: configurable port for the cover art server (default `52312`, set to `0` to disable)
- Automatic port fallback: tries configured port, then +1 and +2 on bind failure, falls back to file path mode silently

### Changed

- `cover-art` output now provides an HTTP URL instead of a local file path when the server is running
- Renamed "Cover Art Path" display name to "Cover Art"
- Release builds now exclude `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` (provided by InfoPanel host), reducing plugin download size from ~25MB to ~67KB

## [1.1.0] - 2026-03-02

### Added

- Session prioritization: dedicated music players (Spotify, VLC, etc.) now take priority over browser tabs when multiple media sessions are active
- `PrioritySources` INI setting: configurable comma-separated list of app names to prioritize, ordered by preference
- `SessionsChanged` event handling: the plugin now reacts to sessions being added or removed, not just the OS "current session" changing

## [1.0.0] - 2026-03-02

### Added

- Initial release of InfoPanel.Media plugin
- Universal media tracking via Windows GSMTC (Global System Media Transport Controls) API
- Support for any GSMTC-compatible media source (Spotify, browsers, VLC, Windows Media Player, foobar2000, AIMP, MPC-HC, etc.)
- 7 PluginText entries: current-track, artist, album, elapsed-time, remaining-time, cover-art, source-app
- 3 PluginSensor entries: track-progress (%), session-state, playback-state
- Album art extraction to temp file with SHA256-based change detection
- 1Hz progress estimation with interpolation between GSMTC timeline events
- Source app resolution with friendly names for common media players
- INI-based configuration: MaxDisplayLength, NoTrackMessage, PausedMessage, NoTrackArtistMessage
- Auto-creation of config file with defaults on first run
- Reentrant Initialize/Close lifecycle support
