namespace InfoPanel.Media.Models;

public sealed record PlaybackInfo(
    string? TrackName,
    string? ArtistName,
    string? AlbumName,
    string? CoverArtPath,
    long ProgressMs,
    long DurationMs,
    bool IsPlaying,
    string? SourceApp,
    bool HasTrack
);
