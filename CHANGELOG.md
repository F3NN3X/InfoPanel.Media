# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
