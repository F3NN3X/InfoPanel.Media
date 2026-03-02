using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using InfoPanel.Media.Models;

namespace InfoPanel.Media.Services;

public sealed class StatusImageService : IDisposable
{
    private readonly string? _playImagePath;
    private readonly string? _pauseImagePath;
    private readonly string? _idleImagePath;
    private readonly byte[]? _playImageData;
    private readonly byte[]? _pauseImageData;
    private readonly byte[]? _idleImageData;
    private readonly int _overlayScale;
    private readonly bool _playEnabled;
    private readonly bool _pauseEnabled;
    private readonly bool _idleEnabled;

    private long _lastCoverArtVersion = -1;
    private float _lastPlaybackState = -1;
    private float _lastSessionState = -1;
    private bool _disposed;

    public static readonly string StatusFilePath =
        Path.Combine(Path.GetTempPath(), "infopanel-media-status.png");

    public long StatusVersion { get; private set; }

    public bool HasAnyStatusImages { get; }

    /// <summary>The last composited image bytes (Replace/Overlay mode). Ready for in-memory HTTP serving.</summary>
    public byte[]? LastCompositeData { get; private set; }

    public StatusImageService(string pluginDir, string playImage, string pauseImage, string idleImage,
        int overlayScale, string[] enabledStates)
    {
        _overlayScale = Math.Clamp(overlayScale, 1, 100);

        _playEnabled = enabledStates.Any(s => s.Equals("Play", StringComparison.OrdinalIgnoreCase));
        _pauseEnabled = enabledStates.Any(s => s.Equals("Pause", StringComparison.OrdinalIgnoreCase));
        _idleEnabled = enabledStates.Any(s => s.Equals("Idle", StringComparison.OrdinalIgnoreCase));

        _playImagePath = ResolvePath(pluginDir, playImage);
        _pauseImagePath = ResolvePath(pluginDir, pauseImage);
        _idleImagePath = ResolvePath(pluginDir, idleImage);

        HasAnyStatusImages = _playImagePath != null || _pauseImagePath != null || _idleImagePath != null;

        // Pre-load icon files into memory for instant HTTP serving
        _playImageData = _playImagePath != null ? ReadFileWithSharing(_playImagePath) : null;
        _pauseImageData = _pauseImagePath != null ? ReadFileWithSharing(_pauseImagePath) : null;
        _idleImageData = _idleImagePath != null ? ReadFileWithSharing(_idleImagePath) : null;

        Debug.WriteLine($"[Media] StatusImageService created - Play: {_playImagePath ?? "none"} (enabled={_playEnabled}), " +
            $"Pause: {_pauseImagePath ?? "none"} (enabled={_pauseEnabled}), " +
            $"Idle: {_idleImagePath ?? "none"} (enabled={_idleEnabled}), " +
            $"OverlayScale: {_overlayScale}%, HasAny: {HasAnyStatusImages}");
    }

    /// <summary>Returns the status image path for the current state. Always returns an icon if one exists.</summary>
    public string? GetStatusImagePath(float playbackState, float sessionState)
    {
        // idle: NoSession and not playing
        if (sessionState == (float)SessionState.NoSession && playbackState == 0.0f)
            return _idleImagePath;

        // playing
        if (playbackState == 2.0f)
            return _playImagePath;

        // paused
        if (playbackState == 1.0f)
            return _pauseImagePath;

        return null;
    }

    /// <summary>Returns pre-loaded status image bytes for the current state. Always returns data if an icon exists. Used for in-memory HTTP serving.</summary>
    public byte[]? GetStatusImageData(float playbackState, float sessionState)
    {
        if (sessionState == (float)SessionState.NoSession && playbackState == 0.0f)
            return _idleImageData;

        if (playbackState == 2.0f)
            return _playImageData;

        if (playbackState == 1.0f)
            return _pauseImageData;

        return null;
    }

    /// <summary>Returns the status image path filtered by StatusImageStates. Used for compositing (Replace/Overlay).</summary>
    private string? GetFilteredStatusImagePath(float playbackState, float sessionState)
    {
        if (sessionState == (float)SessionState.NoSession && playbackState == 0.0f)
            return _idleEnabled ? _idleImagePath : null;

        if (playbackState == 2.0f)
            return _playEnabled ? _playImagePath : null;

        if (playbackState == 1.0f)
            return _pauseEnabled ? _pauseImagePath : null;

        return null;
    }

    public bool UpdateStatusImage(string? coverArtFilePath, long coverArtVersion, float playbackState, float sessionState, StatusImageMode mode)
    {
        if (_disposed) return false;

        // Change detection
        if (coverArtVersion == _lastCoverArtVersion &&
            playbackState == _lastPlaybackState &&
            sessionState == _lastSessionState)
        {
            return false;
        }

        var statusImagePath = GetFilteredStatusImagePath(playbackState, sessionState);

        try
        {
            var success = mode switch
            {
                StatusImageMode.Replace => ComposeReplace(coverArtFilePath, statusImagePath),
                StatusImageMode.Overlay => ComposeOverlay(coverArtFilePath, statusImagePath),
                _ => false
            };

            // Only mark change as handled after successful compositing,
            // so transient failures (file locks, etc.) are retried next cycle
            if (success)
            {
                _lastCoverArtVersion = coverArtVersion;
                _lastPlaybackState = playbackState;
                _lastSessionState = sessionState;
            }

            return success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Error compositing status image: {ex.Message}");
            return false;
        }
    }

    private bool ComposeReplace(string? coverArtFilePath, string? statusImagePath)
    {
        // If we have a status image for this state, use it
        if (statusImagePath != null && File.Exists(statusImagePath))
        {
            var bytes = ReadFileWithSharing(statusImagePath);
            WriteFileWithSharing(StatusFilePath, bytes);
            LastCompositeData = bytes;
            StatusVersion++;
            Debug.WriteLine($"[Media] Status image replaced with: {statusImagePath}");
            return true;
        }

        // Fallback: copy original cover art
        if (coverArtFilePath != null && File.Exists(coverArtFilePath))
        {
            var bytes = ReadFileWithSharing(coverArtFilePath);
            WriteFileWithSharing(StatusFilePath, bytes);
            LastCompositeData = bytes;
            StatusVersion++;
            Debug.WriteLine("[Media] Status image fallback to cover art (no status image for state).");
            return true;
        }

        return false;
    }

    private bool ComposeOverlay(string? coverArtFilePath, string? statusImagePath)
    {
        // If no status image for this state, just copy cover art
        if (statusImagePath == null || !File.Exists(statusImagePath))
        {
            if (coverArtFilePath != null && File.Exists(coverArtFilePath))
            {
                var bytes = ReadFileWithSharing(coverArtFilePath);
                WriteFileWithSharing(StatusFilePath, bytes);
                LastCompositeData = bytes;
                StatusVersion++;
                Debug.WriteLine("[Media] Overlay fallback to cover art (no status image for state).");
                return true;
            }
            return false;
        }

        // If no cover art, just use the status image
        if (coverArtFilePath == null || !File.Exists(coverArtFilePath))
        {
            var bytes = ReadFileWithSharing(statusImagePath);
            WriteFileWithSharing(StatusFilePath, bytes);
            LastCompositeData = bytes;
            StatusVersion++;
            Debug.WriteLine("[Media] Overlay with status image only (no cover art).");
            return true;
        }

        // Load bitmaps from byte arrays to avoid GDI+ file locks
        var coverBytes = ReadFileWithSharing(coverArtFilePath);
        var statusBytes = ReadFileWithSharing(statusImagePath);

        using var coverStream = new MemoryStream(coverBytes);
        using var statusStream = new MemoryStream(statusBytes);
        using var coverBmp = new Bitmap(coverStream);
        using var statusBmp = new Bitmap(statusStream);
        using var result = new Bitmap(coverBmp.Width, coverBmp.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Draw cover art
        g.DrawImage(coverBmp, 0, 0, coverBmp.Width, coverBmp.Height);

        // Scale overlay to percentage of smaller dimension
        var smallerDim = Math.Min(coverBmp.Width, coverBmp.Height);
        var overlaySize = (int)(smallerDim * _overlayScale / 100.0);
        if (overlaySize < 1) overlaySize = 1;

        // Center the overlay
        var overlayX = (coverBmp.Width - overlaySize) / 2;
        var overlayY = (coverBmp.Height - overlaySize) / 2;

        g.DrawImage(statusBmp, overlayX, overlayY, overlaySize, overlaySize);

        // Save to MemoryStream first, then write with sharing-safe FileStream
        using var outputStream = new MemoryStream();
        result.Save(outputStream, ImageFormat.Png);
        var outputBytes = outputStream.ToArray();
        WriteFileWithSharing(StatusFilePath, outputBytes);
        LastCompositeData = outputBytes;
        StatusVersion++;

        Debug.WriteLine($"[Media] Overlay composited: {statusImagePath} ({overlaySize}px) on cover art ({coverBmp.Width}x{coverBmp.Height}).");
        return true;
    }

    /// <summary>Reads a file with sharing flags that allow concurrent writers (e.g. thumbnail extraction).</summary>
    private static byte[] ReadFileWithSharing(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Writes a file with sharing flags that allow concurrent readers (e.g. HTTP server).</summary>
    private static void WriteFileWithSharing(string path, byte[] data)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        fs.Write(data);
    }

    /// <summary>Copies a file using sharing-safe read/write to avoid conflicts with concurrent access.</summary>
    private static void CopyFileWithSharing(string source, string destination)
    {
        var bytes = ReadFileWithSharing(source);
        WriteFileWithSharing(destination, bytes);
    }

    private static string? ResolvePath(string pluginDir, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var resolved = Path.IsPathRooted(imagePath)
            ? imagePath
            : Path.Combine(pluginDir, imagePath);

        if (File.Exists(resolved))
        {
            Debug.WriteLine($"[Media] Status image resolved: {imagePath} -> {resolved}");
            return resolved;
        }

        Debug.WriteLine($"[Media] Status image not found: {resolved}");
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (File.Exists(StatusFilePath))
                File.Delete(StatusFilePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Error cleaning up status image file: {ex.Message}");
        }

        Debug.WriteLine("[Media] StatusImageService disposed.");
    }
}
