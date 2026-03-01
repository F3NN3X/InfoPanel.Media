using System.Diagnostics;
using System.Security.Cryptography;
using InfoPanel.Media.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace InfoPanel.Media.Services;

public sealed class MediaPlaybackService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    // Playback state tracking
    private string? _lastTrackName;
    private string? _lastArtistName;
    private string? _lastAlbumName;
    private string? _lastCoverArtPath;
    private string? _lastSourceApp;
    private long _lastKnownPositionMs;
    private long _lastDurationMs;
    private bool _isPlaying;
    private DateTime _lastPositionUpdateTime = DateTime.UtcNow;
    private byte[]? _lastThumbnailHash;
    private bool _disposed;

    private static readonly string CoverArtFilePath =
        Path.Combine(Path.GetTempPath(), "infopanel-media-cover.png");

    private static readonly Dictionary<string, string> FriendlyAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spotify"] = "Spotify",
        ["spotify.exe"] = "Spotify",
        ["Spotify.exe"] = "Spotify",
        ["chrome"] = "Chrome",
        ["chrome.exe"] = "Chrome",
        ["msedge"] = "Edge",
        ["msedge.exe"] = "Edge",
        ["firefox"] = "Firefox",
        ["firefox.exe"] = "Firefox",
        ["vlc"] = "VLC",
        ["vlc.exe"] = "VLC",
        ["wmplayer"] = "Windows Media Player",
        ["wmplayer.exe"] = "Windows Media Player",
        ["Music.UI"] = "Groove Music",
        ["Microsoft.ZuneMusic"] = "Groove Music",
        ["foobar2000"] = "foobar2000",
        ["foobar2000.exe"] = "foobar2000",
        ["AIMP"] = "AIMP",
        ["AIMP.exe"] = "AIMP",
        ["mpc-hc64"] = "MPC-HC",
        ["mpc-hc64.exe"] = "MPC-HC",
        ["mpc-hc"] = "MPC-HC",
        ["mpc-hc.exe"] = "MPC-HC",
    };

    // Events for playback updates
    public event EventHandler<PlaybackInfo>? PlaybackUpdated;
    public event EventHandler<string>? PlaybackError;
    public event EventHandler<SessionState>? SessionStateChanged;

    public async Task InitializeAsync()
    {
        try
        {
            Debug.WriteLine($"[Media] Initializing GSMTC session manager at UTC: {DateTime.UtcNow:o}");
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;

            var session = _sessionManager.GetCurrentSession();
            AttachSession(session);

            if (session != null)
            {
                SessionStateChanged?.Invoke(this, SessionState.SessionActive);
                Debug.WriteLine($"[Media] Initial session found: {session.SourceAppUserModelId}");
            }
            else
            {
                SessionStateChanged?.Invoke(this, SessionState.NoSession);
                Debug.WriteLine("[Media] No initial media session found.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Failed to initialize GSMTC: {ex.Message}");
            SessionStateChanged?.Invoke(this, SessionState.Error);
            PlaybackError?.Invoke(this, "Media session initialization failed");
        }
    }

    public void UpdatePlaybackInfo()
    {
        if (_disposed) return;

        if (_currentSession == null)
        {
            var noSessionInfo = new PlaybackInfo(
                TrackName: null,
                ArtistName: null,
                AlbumName: null,
                CoverArtPath: null,
                ProgressMs: 0,
                DurationMs: 0,
                IsPlaying: false,
                SourceApp: null,
                HasTrack: false
            );
            PlaybackUpdated?.Invoke(this, noSessionInfo);
            return;
        }

        // Estimate progress between GSMTC timeline events
        long estimatedPositionMs = _lastKnownPositionMs;
        if (_isPlaying)
        {
            var elapsed = (DateTime.UtcNow - _lastPositionUpdateTime).TotalMilliseconds;
            estimatedPositionMs = _lastKnownPositionMs + (long)elapsed;
            if (_lastDurationMs > 0)
            {
                estimatedPositionMs = Math.Clamp(estimatedPositionMs, 0, _lastDurationMs);
            }
        }

        var info = new PlaybackInfo(
            TrackName: _lastTrackName,
            ArtistName: _lastArtistName,
            AlbumName: _lastAlbumName,
            CoverArtPath: _lastCoverArtPath,
            ProgressMs: estimatedPositionMs,
            DurationMs: _lastDurationMs,
            IsPlaying: _isPlaying,
            SourceApp: _lastSourceApp,
            HasTrack: _lastTrackName != null
        );

        Debug.WriteLine($"[Media] Update - Track: {_lastTrackName}, " +
            $"Elapsed: {TimeSpan.FromMilliseconds(estimatedPositionMs):mm\\:ss}, " +
            $"Playing: {_isPlaying}, Source: {_lastSourceApp}");

        PlaybackUpdated?.Invoke(this, info);
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        DetachSession();

        _currentSession = session;
        if (_currentSession == null)
        {
            ResetState();
            return;
        }

        _lastSourceApp = ResolveSourceApp(_currentSession.SourceAppUserModelId);

        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        // Sync initial state
        SyncPlaybackInfo();
        SyncTimelineProperties();
        _ = SyncMediaPropertiesAsync();
    }

    private void DetachSession()
    {
        if (_currentSession == null) return;

        _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _currentSession = null;
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        Debug.WriteLine($"[Media] Current session changed at UTC: {DateTime.UtcNow:o}");
        var session = sender.GetCurrentSession();
        AttachSession(session);

        if (session != null)
        {
            SessionStateChanged?.Invoke(this, SessionState.SessionActive);
        }
        else
        {
            SessionStateChanged?.Invoke(this, SessionState.NoSession);
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Debug.WriteLine($"[Media] Media properties changed at UTC: {DateTime.UtcNow:o}");
        _ = SyncMediaPropertiesAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Debug.WriteLine($"[Media] Playback info changed at UTC: {DateTime.UtcNow:o}");
        SyncPlaybackInfo();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        Debug.WriteLine($"[Media] Timeline properties changed at UTC: {DateTime.UtcNow:o}");
        SyncTimelineProperties();
    }

    private async Task SyncMediaPropertiesAsync()
    {
        if (_currentSession == null) return;

        try
        {
            var mediaProps = await _currentSession.TryGetMediaPropertiesAsync();
            if (mediaProps == null) return;

            _lastTrackName = !string.IsNullOrEmpty(mediaProps.Title) ? mediaProps.Title : null;
            _lastArtistName = !string.IsNullOrEmpty(mediaProps.Artist) ? mediaProps.Artist : null;
            _lastAlbumName = !string.IsNullOrEmpty(mediaProps.AlbumTitle) ? mediaProps.AlbumTitle : null;

            Debug.WriteLine($"[Media] Synced media props - Track: {_lastTrackName}, Artist: {_lastArtistName}, Album: {_lastAlbumName}");

            if (mediaProps.Thumbnail != null)
            {
                await ExtractThumbnailAsync(mediaProps.Thumbnail);
            }
            else
            {
                _lastCoverArtPath = null;
                _lastThumbnailHash = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Error syncing media properties: {ex.Message}");
        }
    }

    private void SyncPlaybackInfo()
    {
        if (_currentSession == null) return;

        try
        {
            var playbackInfo = _currentSession.GetPlaybackInfo();
            if (playbackInfo == null) return;

            _isPlaying = playbackInfo.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Re-sync position timestamp when play state changes
            _lastPositionUpdateTime = DateTime.UtcNow;

            Debug.WriteLine($"[Media] Synced playback info - Playing: {_isPlaying}, Status: {playbackInfo.PlaybackStatus}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Error syncing playback info: {ex.Message}");
        }
    }

    private void SyncTimelineProperties()
    {
        if (_currentSession == null) return;

        try
        {
            var timeline = _currentSession.GetTimelineProperties();
            if (timeline == null) return;

            _lastKnownPositionMs = (long)timeline.Position.TotalMilliseconds;
            _lastDurationMs = (long)timeline.EndTime.TotalMilliseconds;
            _lastPositionUpdateTime = DateTime.UtcNow;

            Debug.WriteLine($"[Media] Synced timeline - Position: {timeline.Position:mm\\:ss}, " +
                $"Duration: {timeline.EndTime:mm\\:ss}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Error syncing timeline properties: {ex.Message}");
        }
    }

    private async Task ExtractThumbnailAsync(IRandomAccessStreamReference thumbnailRef)
    {
        try
        {
            using var stream = await thumbnailRef.OpenReadAsync();
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            var hash = SHA256.HashData(bytes);

            if (_lastThumbnailHash != null && hash.AsSpan().SequenceEqual(_lastThumbnailHash))
            {
                Debug.WriteLine("[Media] Thumbnail unchanged, skipping write.");
                return;
            }

            _lastThumbnailHash = hash;
            await File.WriteAllBytesAsync(CoverArtFilePath, bytes);
            _lastCoverArtPath = CoverArtFilePath;

            Debug.WriteLine($"[Media] Thumbnail saved to: {CoverArtFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Error extracting thumbnail: {ex.Message}");
            _lastCoverArtPath = null;
        }
    }

    private static string ResolveSourceApp(string? sourceAppUserModelId)
    {
        if (string.IsNullOrEmpty(sourceAppUserModelId))
            return "Unknown";

        if (FriendlyAppNames.TryGetValue(sourceAppUserModelId, out var friendly))
            return friendly;

        // Try matching against the last segment (for UWP-style IDs like "Microsoft.ZuneMusic_8wekyb...")
        var lastSegment = sourceAppUserModelId.Split('!', '_')[0];
        var lastDotSegment = lastSegment.Contains('.') ? lastSegment[(lastSegment.LastIndexOf('.') + 1)..] : lastSegment;

        if (FriendlyAppNames.TryGetValue(lastDotSegment, out var segmentFriendly))
            return segmentFriendly;

        // Strip .exe suffix and title-case
        var cleaned = lastDotSegment.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrEmpty(cleaned) ? "Unknown" : char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }

    private void ResetState()
    {
        _lastTrackName = null;
        _lastArtistName = null;
        _lastAlbumName = null;
        _lastCoverArtPath = null;
        _lastSourceApp = null;
        _lastKnownPositionMs = 0;
        _lastDurationMs = 0;
        _isPlaying = false;
        _lastThumbnailHash = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DetachSession();

        if (_sessionManager != null)
        {
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _sessionManager = null;
        }

        ResetState();
        Debug.WriteLine("[Media] MediaPlaybackService disposed.");
    }
}
