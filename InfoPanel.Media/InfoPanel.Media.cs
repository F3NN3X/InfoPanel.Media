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

    // Services
    private MediaPlaybackService? _playbackService;
    private CoverArtServer? _coverArtServer;

    // Configuration
    private string? _configFilePath;
    private int _maxDisplayLength = 20;
    private string _noTrackMessage = "No music playing";
    private string _pausedMessage = "";
    private string _noTrackArtistMessage = "-";
    private string[] _prioritySources = [];
    private int _coverArtPort = 52312;

    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    public MediaPlugin()
        : base("media-plugin", "Media", "Displays current media playback information from any source. Version: 1.2.1")
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

        _coverArtServer = new CoverArtServer(MediaPlaybackService.CoverArtFilePath, _coverArtPort);
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
            parser.WriteFile(_configFilePath, config);
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

                if (configUpdated)
                {
                    parser.WriteFile(_configFilePath, config);
                    Debug.WriteLine("[Media] Added missing settings to config.");
                }

                Debug.WriteLine($"[Media] Loaded config - MaxDisplayLength: {_maxDisplayLength}, " +
                    $"NoTrackMessage: '{_noTrackMessage}', PausedMessage: '{_pausedMessage}', " +
                    $"NoTrackArtistMessage: '{_noTrackArtistMessage}', " +
                    $"PrioritySources: [{string.Join(", ", _prioritySources)}], " +
                    $"CoverArtPort: {_coverArtPort}");
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
            _trackProgress, _sessionState, _playbackState, _coverArt, _sourceApp]);
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
            _coverArt.Value = string.Empty;
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
            _coverArt.Value = _coverArtServer?.CoverArtUrl is { } url
                ? $"{url}?v={_playbackService?.CoverArtVersion}"
                : info.CoverArtPath ?? string.Empty;
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
            _coverArt.Value = _coverArtServer?.CoverArtUrl is { } url
                ? $"{url}?v={_playbackService?.CoverArtVersion}"
                : info.CoverArtPath ?? string.Empty;
            _sourceApp.Value = info.SourceApp ?? "-";
            _playbackState.Value = info.IsPlaying ? 2.0F : 1.0F;
        }

        _currentTrack.Value = trackName;
        _artist.Value = artistName;
        _album.Value = albumName;
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
        _sourceApp.Value = "-";
        _playbackState.Value = 0.0F;
        Debug.WriteLine($"[Media] Set default values: {message}");
    }

    public override void Close()
    {
        Debug.WriteLine($"[Media] Close called at UTC: {DateTime.UtcNow:o}");

        _coverArtServer?.Dispose();
        _coverArtServer = null;

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
