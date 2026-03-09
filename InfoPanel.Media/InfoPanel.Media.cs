using System.Diagnostics;
using System.Reflection;
using InfoPanel.Media.Models;
using InfoPanel.Media.Services;
using InfoPanel.Plugins;
using IniParser;
using IniParser.Model;

namespace InfoPanel.Media;

public sealed class MediaPlugin : BasePlugin
{
    // UI display elements (PluginText) for InfoPanel
    private readonly PluginText _currentTrack = new("current-track", "Current Track", "-");
    private readonly PluginText _artist = new("artist", "Artist", "-");
    private readonly PluginText _album = new("album", "Album", "-");
    private readonly PluginText _elapsedTime = new("elapsed-time", "Elapsed Time", "00:00");
    private readonly PluginText _remainingTime = new("remaining-time", "Remaining Time", "00:00");
    private readonly PluginText _coverArt = new("cover-art", "Cover Art", "");
    private readonly PluginText _sourceApp = new("source-app", "Source App", "-");

    // UI display elements (PluginSensor) for InfoPanel
    private readonly PluginSensor _trackProgress = new("track-progress", "Track Progress (%)", 0.0F);
    private readonly PluginSensor _sessionState = new("session-state", "Session State", (float)SessionState.NoSession);
    private readonly PluginSensor _playbackState = new("playback-state", "Playback State", 0.0F);

    // UI display elements for status image (conditionally registered)
    private readonly PluginText _statusImage = new("status-image", "Status Image", "");

    // Services
    private MediaPlaybackService? _playbackService;
    private CoverArtServer? _coverArtServer;
    private StatusImageService? _statusImageService;

    // Configuration
    private string? _configFilePath;
    private int _maxDisplayLength = 20;
    private string _noTrackMessage = "No music playing";
    private string _pausedMessage = "";
    private string _noTrackArtistMessage = "-";
    private string[] _prioritySources = [];
    private int _coverArtPort = 52312;
    private StatusImageMode _statusImageMode = StatusImageMode.Overlay;
    private string _playImage = "play.png";
    private string _pauseImage = "pause.png";
    private string _idleImage = "idle.png";
    private int _overlayScale = 50;
    private string[] _statusImageStates = ["Play", "Pause", "Idle"];

    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    public MediaPlugin()
        : base("media-plugin", "Media", "Displays current media playback information from any source. Version: 1.3.1")
    {
    }

    public override string? ConfigFilePath => _configFilePath;

    public override void Initialize()
    {
        Debug.WriteLine($"[Media] Initialize called at UTC: {DateTime.UtcNow:o}");

        Assembly assembly = Assembly.GetExecutingAssembly();
        string basePath = assembly.ManifestModule.FullyQualifiedName;
        _configFilePath = $"{basePath}.ini";
        Debug.WriteLine($"[Media] Config file path: {_configFilePath}");

        LoadConfigFile();

        // Clean up previous services for reentrancy
        _coverArtServer?.Dispose();
        _coverArtServer = null;
        _statusImageService?.Dispose();
        _statusImageService = null;

        if (_playbackService != null)
        {
            _playbackService.PlaybackUpdated -= OnPlaybackUpdated;
            _playbackService.PlaybackError -= OnPlaybackError;
            _playbackService.SessionStateChanged -= OnSessionStateChanged;
            _playbackService.Dispose();
        }

        _playbackService = new MediaPlaybackService(_prioritySources);
        _playbackService.PlaybackUpdated += OnPlaybackUpdated;
        _playbackService.PlaybackError += OnPlaybackError;
        _playbackService.SessionStateChanged += OnSessionStateChanged;

        // Create status image service
        var pluginDir = Path.GetDirectoryName(assembly.ManifestModule.FullyQualifiedName)!;
        _statusImageService = new StatusImageService(pluginDir, _playImage, _pauseImage, _idleImage, _overlayScale, _statusImageStates);

        if (_statusImageMode != StatusImageMode.Off && !_statusImageService.HasAnyStatusImages)
        {
            Debug.WriteLine($"[Media] StatusImageMode was {_statusImageMode} but no status images found — reverting to Off.");
            _statusImageMode = StatusImageMode.Off;
        }

        // Cover art server file path depends on mode
        var coverArtFilePath = _statusImageMode is StatusImageMode.Replace or StatusImageMode.Overlay
            ? StatusImageService.StatusFilePath
            : MediaPlaybackService.CoverArtFilePath;

        _coverArtServer = new CoverArtServer(coverArtFilePath, _coverArtPort);
        _coverArtServer.Start();

        // Bridge async initialization from sync context
        Task.Run(async () =>
        {
            await _playbackService.InitializeAsync();
        }).GetAwaiter().GetResult();

        Debug.WriteLine("[Media] Plugin initialized.");
    }

    private void LoadConfigFile()
    {
        var parser = new FileIniDataParser();
        IniData config;

        if (!File.Exists(_configFilePath))
        {
            config = new IniData();
            config["Media Plugin"]["MaxDisplayLength"] = "20";
            config["Media Plugin"]["NoTrackMessage"] = "No music playing";
            config["Media Plugin"]["PausedMessage"] = "";
            config["Media Plugin"]["NoTrackArtistMessage"] = "-";
            config["Media Plugin"]["PrioritySources"] = "Spotify,Apple Music,VLC,foobar2000,AIMP,Groove Music,Windows Media Player,MPC-HC";
            config["Media Plugin"]["CoverArtPort"] = "52312";
            config["Media Plugin"]["StatusImageMode"] = "Overlay";
            config["Media Plugin"]["PlayImage"] = "play.png";
            config["Media Plugin"]["PauseImage"] = "pause.png";
            config["Media Plugin"]["IdleImage"] = "idle.png";
            config["Media Plugin"]["OverlayScale"] = "50";
            config["Media Plugin"]["StatusImageStates"] = "Play,Pause,Idle";
            parser.WriteFile(_configFilePath, config);
            _statusImageMode = StatusImageMode.Overlay;
            _prioritySources = ["Spotify", "Apple Music", "VLC", "foobar2000", "AIMP", "Groove Music", "Windows Media Player", "MPC-HC"];
            Debug.WriteLine("[Media] Config file created with defaults.");
        }
        else
        {
            try
            {
                using var fileStream = new FileStream(_configFilePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(fileStream);

                string fileContent = reader.ReadToEnd();
                config = parser.Parser.Parse(fileContent);

                bool configUpdated = false;

                if (!config["Media Plugin"].ContainsKey("MaxDisplayLength") ||
                    !int.TryParse(config["Media Plugin"]["MaxDisplayLength"], out int maxLength) ||
                    maxLength <= 0)
                {
                    config["Media Plugin"]["MaxDisplayLength"] = "20";
                    _maxDisplayLength = 20;
                    configUpdated = true;
                    Debug.WriteLine("[Media] MaxDisplayLength added or corrected to 20 in config.");
                }
                else
                {
                    _maxDisplayLength = maxLength;
                }

                _noTrackMessage = config["Media Plugin"]["NoTrackMessage"] ?? "No music playing";
                _pausedMessage = config["Media Plugin"]["PausedMessage"] ?? "";
                _noTrackArtistMessage = config["Media Plugin"]["NoTrackArtistMessage"] ?? "-";

                if (!config["Media Plugin"].ContainsKey("NoTrackMessage"))
                {
                    config["Media Plugin"]["NoTrackMessage"] = _noTrackMessage;
                    configUpdated = true;
                }
                if (!config["Media Plugin"].ContainsKey("PausedMessage"))
                {
                    config["Media Plugin"]["PausedMessage"] = _pausedMessage;
                    configUpdated = true;
                }
                if (!config["Media Plugin"].ContainsKey("NoTrackArtistMessage"))
                {
                    config["Media Plugin"]["NoTrackArtistMessage"] = _noTrackArtistMessage;
                    configUpdated = true;
                }

                if (!config["Media Plugin"].ContainsKey("PrioritySources"))
                {
                    config["Media Plugin"]["PrioritySources"] = "Spotify,Apple Music,VLC,foobar2000,AIMP,Groove Music,Windows Media Player,MPC-HC";
                    configUpdated = true;
                }

                var priorityRaw = config["Media Plugin"]["PrioritySources"] ?? "";
                _prioritySources = string.IsNullOrWhiteSpace(priorityRaw)
                    ? []
                    : priorityRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (!config["Media Plugin"].ContainsKey("CoverArtPort") ||
                    !int.TryParse(config["Media Plugin"]["CoverArtPort"], out int coverArtPort) ||
                    coverArtPort < 0)
                {
                    config["Media Plugin"]["CoverArtPort"] = "52312";
                    _coverArtPort = 52312;
                    configUpdated = true;
                    Debug.WriteLine("[Media] CoverArtPort added or corrected to 52312 in config.");
                }
                else
                {
                    _coverArtPort = coverArtPort;
                }

                // StatusImageMode
                if (config["Media Plugin"].ContainsKey("StatusImageMode") &&
                    Enum.TryParse<StatusImageMode>(config["Media Plugin"]["StatusImageMode"], ignoreCase: true, out var parsedMode))
                {
                    _statusImageMode = parsedMode;
                }
                else
                {
                    config["Media Plugin"]["StatusImageMode"] = "Overlay";
                    _statusImageMode = StatusImageMode.Overlay;
                    configUpdated = true;
                }

                // PlayImage
                _playImage = config["Media Plugin"]["PlayImage"] ?? "play.png";
                if (!config["Media Plugin"].ContainsKey("PlayImage"))
                {
                    config["Media Plugin"]["PlayImage"] = _playImage;
                    configUpdated = true;
                }

                // PauseImage
                _pauseImage = config["Media Plugin"]["PauseImage"] ?? "pause.png";
                if (!config["Media Plugin"].ContainsKey("PauseImage"))
                {
                    config["Media Plugin"]["PauseImage"] = _pauseImage;
                    configUpdated = true;
                }

                // IdleImage
                _idleImage = config["Media Plugin"]["IdleImage"] ?? "idle.png";
                if (!config["Media Plugin"].ContainsKey("IdleImage"))
                {
                    config["Media Plugin"]["IdleImage"] = _idleImage;
                    configUpdated = true;
                }

                // OverlayScale
                if (!config["Media Plugin"].ContainsKey("OverlayScale") ||
                    !int.TryParse(config["Media Plugin"]["OverlayScale"], out int overlayScale) ||
                    overlayScale < 1 || overlayScale > 100)
                {
                    config["Media Plugin"]["OverlayScale"] = "50";
                    _overlayScale = 50;
                    configUpdated = true;
                }
                else
                {
                    _overlayScale = overlayScale;
                }

                // StatusImageStates
                if (!config["Media Plugin"].ContainsKey("StatusImageStates"))
                {
                    config["Media Plugin"]["StatusImageStates"] = "Play,Pause,Idle";
                    configUpdated = true;
                }

                var statesRaw = config["Media Plugin"]["StatusImageStates"] ?? "Play,Pause,Idle";
                _statusImageStates = string.IsNullOrWhiteSpace(statesRaw)
                    ? []
                    : statesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (configUpdated)
                {
                    parser.WriteFile(_configFilePath, config);
                    Debug.WriteLine("[Media] Added missing settings to config.");
                }

                Debug.WriteLine($"[Media] Loaded config - MaxDisplayLength: {_maxDisplayLength}, " +
                    $"NoTrackMessage: '{_noTrackMessage}', PausedMessage: '{_pausedMessage}', " +
                    $"NoTrackArtistMessage: '{_noTrackArtistMessage}', " +
                    $"PrioritySources: [{string.Join(", ", _prioritySources)}], " +
                    $"CoverArtPort: {_coverArtPort}, " +
                    $"StatusImageMode: {_statusImageMode}, PlayImage: '{_playImage}', " +
                    $"PauseImage: '{_pauseImage}', IdleImage: '{_idleImage}', OverlayScale: {_overlayScale}, " +
                    $"StatusImageStates: [{string.Join(", ", _statusImageStates)}]");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Media] Error reading config file: {ex.Message}");
                _sessionState.Value = (float)SessionState.Error;
            }
        }
    }

    public override void Load(List<IPluginContainer> containers)
    {
        var container = new PluginContainer("Media");
        container.Entries.AddRange([_currentTrack, _artist, _album, _elapsedTime, _remainingTime,
            _trackProgress, _sessionState, _playbackState, _coverArt, _sourceApp, _statusImage]);
        containers.Add(container);
    }

    public override void Update()
    {
        UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task UpdateAsync(CancellationToken cancellationToken)
    {
        Debug.WriteLine($"[Media] UpdateAsync called at UTC: {DateTime.UtcNow:o}");

        if (_playbackService != null)
        {
            await Task.Run(() => _playbackService.UpdatePlaybackInfo(), cancellationToken);
        }
    }

    private void OnSessionStateChanged(object? sender, SessionState state) =>
        _sessionState.Value = (float)state;

    private void OnPlaybackUpdated(object? sender, PlaybackInfo info)
    {
        string trackName, artistName, albumName;

        if (!info.HasTrack)
        {
            trackName = _noTrackMessage;
            artistName = _noTrackArtistMessage;
            albumName = "-";
            _elapsedTime.Value = "00:00";
            _remainingTime.Value = "00:00";
            _trackProgress.Value = 0.0F;
            _sourceApp.Value = info.SourceApp ?? "-";
            _playbackState.Value = 0.0F;
        }
        else if (!info.IsPlaying && !string.IsNullOrEmpty(_pausedMessage))
        {
            trackName = _pausedMessage;
            artistName = _noTrackArtistMessage;
            albumName = "-";
            _elapsedTime.Value = TimeSpan.FromMilliseconds(info.ProgressMs).ToString(@"mm\:ss");
            _remainingTime.Value = TimeSpan.FromMilliseconds(info.DurationMs - info.ProgressMs).ToString(@"mm\:ss");
            _trackProgress.Value = info.DurationMs > 0 ? (float)(info.ProgressMs / (double)info.DurationMs * 100) : 0.0F;
            _sourceApp.Value = info.SourceApp ?? "-";
            _playbackState.Value = 1.0F;
        }
        else
        {
            trackName = CutString(info.TrackName ?? "Unknown");
            artistName = CutString(info.ArtistName ?? "Unknown");
            albumName = CutString(info.AlbumName ?? "Unknown");
            _elapsedTime.Value = TimeSpan.FromMilliseconds(info.ProgressMs).ToString(@"mm\:ss");
            _remainingTime.Value = TimeSpan.FromMilliseconds(info.DurationMs - info.ProgressMs).ToString(@"mm\:ss");
            _trackProgress.Value = info.DurationMs > 0 ? (float)(info.ProgressMs / (double)info.DurationMs * 100) : 0.0F;
            _sourceApp.Value = info.SourceApp ?? "-";
            _playbackState.Value = info.IsPlaying ? 2.0F : 1.0F;
        }

        _coverArt.Value = ResolveCoverArt(info);

        if (_statusImageMode != StatusImageMode.Off)
        {
            var statusPath = _statusImageService?.GetStatusImagePath(_playbackState.Value, _sessionState.Value);
            if (_coverArtServer != null)
                _coverArtServer.StatusImageFilePath = statusPath;

            // Push status icon bytes to server for in-memory serving
            _coverArtServer?.SetStatusImageData(_statusImageService?.GetStatusImageData(_playbackState.Value, _sessionState.Value));

            _statusImage.Value = statusPath != null && _coverArtServer?.StatusImageUrl is { } statusUrl
                ? $"{statusUrl}?v={_playbackState.Value:0}_{_sessionState.Value:0}"
                : "";
        }

        _currentTrack.Value = trackName;
        _artist.Value = artistName;
        _album.Value = albumName;
    }

    private string ResolveCoverArt(PlaybackInfo info)
    {
        if (_statusImageMode is StatusImageMode.Replace or StatusImageMode.Overlay)
        {
            _statusImageService?.UpdateStatusImage(
                info.CoverArtPath,
                _playbackService?.CoverArtVersion ?? 0,
                _playbackState.Value,
                _sessionState.Value,
                _statusImageMode);

            // Push composited bytes to server BEFORE returning versioned URL,
            // so the data is ready in memory when InfoPanel fetches
            _coverArtServer?.SetCoverImageData(_statusImageService?.LastCompositeData);

            return _coverArtServer?.CoverArtUrl is { } statusUrl
                ? $"{statusUrl}?v={_statusImageService?.StatusVersion}"
                : StatusImageService.StatusFilePath;
        }

        // Off or Separate: original behavior
        if (!info.HasTrack)
            return string.Empty;

        // Push thumbnail bytes to server for in-memory serving
        _coverArtServer?.SetCoverImageData(_playbackService?.LastThumbnailBytes);

        return _coverArtServer?.CoverArtUrl is { } coverUrl
            ? $"{coverUrl}?v={_playbackService?.CoverArtVersion}"
            : info.CoverArtPath ?? string.Empty;
    }

    private void OnPlaybackError(object? sender, string errorMessage)
    {
        SetDefaultValues(errorMessage);
    }

    private string CutString(string input)
    {
        if (_maxDisplayLength < 4)
            return input.Length > _maxDisplayLength ? input[.._maxDisplayLength] : input;

        return input.Length > _maxDisplayLength ? $"{input[..(_maxDisplayLength - 3)]}..." : input;
    }

    private void SetDefaultValues(string message)
    {
        _currentTrack.Value = message;
        _artist.Value = _noTrackArtistMessage;
        _album.Value = "-";
        _elapsedTime.Value = "00:00";
        _remainingTime.Value = "00:00";
        _trackProgress.Value = 0.0F;
        _coverArt.Value = string.Empty;
        _statusImage.Value = "";
        _sourceApp.Value = "-";
        _playbackState.Value = 0.0F;
        Debug.WriteLine($"[Media] Set default values: {message}");
    }

    public override void Close()
    {
        Debug.WriteLine($"[Media] Close called at UTC: {DateTime.UtcNow:o}");

        _coverArtServer?.Dispose();
        _coverArtServer = null;

        _statusImageService?.Dispose();
        _statusImageService = null;

        if (_playbackService != null)
        {
            _playbackService.PlaybackUpdated -= OnPlaybackUpdated;
            _playbackService.PlaybackError -= OnPlaybackError;
            _playbackService.SessionStateChanged -= OnSessionStateChanged;
            _playbackService.Dispose();
            _playbackService = null;
        }

        _sessionState.Value = (float)SessionState.NoSession;
        SetDefaultValues("Plugin Closed");

        Debug.WriteLine("[Media] Plugin closed.");
    }
}
