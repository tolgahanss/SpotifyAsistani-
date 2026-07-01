using System;

namespace SpotifyAsistani.Models
{
    /// <summary>
    /// Spotify'dan çekilen şarkı bilgisi
    /// </summary>
    public class TrackInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string AlbumArtUrl { get; set; } = string.Empty;
        public int DurationMs { get; set; }
        public int ProgressMs { get; set; }
        public bool IsPlaying { get; set; }

        public string DurationFormatted => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");
        public string ProgressFormatted => TimeSpan.FromMilliseconds(ProgressMs).ToString(@"m\:ss");
        public double ProgressPercent => DurationMs > 0 ? (double)ProgressMs / DurationMs * 100.0 : 0;
    }

    /// <summary>
    /// Yerel veritabanında tutulan şarkı istatistiği
    /// </summary>
    public class TrackStats
    {
        public string SpotifyId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string AlbumArtUrl { get; set; } = string.Empty;
        public int DurationMs { get; set; }
        public int PlayCount { get; set; }
        public long TotalListenedMs { get; set; }
        public DateTime FirstPlayedAt { get; set; }
        public DateTime LastPlayedAt { get; set; }

        public string TotalListenedFormatted
        {
            get
            {
                var ts = TimeSpan.FromMilliseconds(TotalListenedMs);
                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours}sa {ts.Minutes}dk";
                return $"{ts.Minutes}dk {ts.Seconds}sn";
            }
        }
    }

    /// <summary>
    /// Genel dinleme istatistikleri
    /// </summary>
    public class OverallStats
    {
        public int TotalTracks { get; set; }
        public int TotalPlays { get; set; }
        public long TotalListenedMs { get; set; }
        public int TodayPlays { get; set; }

        public string TotalListenedFormatted
        {
            get
            {
                var ts = TimeSpan.FromMilliseconds(TotalListenedMs);
                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours}sa {ts.Minutes}dk";
                return $"{ts.Minutes}dk {ts.Seconds}sn";
            }
        }
    }

    /// <summary>
    /// Spotify çalma listesi bilgisi
    /// </summary>
    public class PlaylistInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int TrackCount { get; set; }
        public string Owner { get; set; } = string.Empty;
    }

    /// <summary>
    /// Spotify ayarları
    /// </summary>
    public class SpotifyConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime TokenExpiry { get; set; }
    }
}
