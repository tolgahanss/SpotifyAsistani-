using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using SpotifyAsistani.Models;

namespace SpotifyAsistani.Services
{
    /// <summary>
    /// SQLite veritabanı servisi - dinleme istatistiklerini yerel olarak tutar
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;

        public DatabaseService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyAsistani");
            Directory.CreateDirectory(appData);
            _dbPath = Path.Combine(appData, "listening_history.db");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS tracks (
                    spotify_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    artist TEXT NOT NULL,
                    album TEXT NOT NULL DEFAULT '',
                    album_art_url TEXT NOT NULL DEFAULT '',
                    duration_ms INTEGER NOT NULL DEFAULT 0,
                    play_count INTEGER NOT NULL DEFAULT 0,
                    total_listened_ms INTEGER NOT NULL DEFAULT 0,
                    first_played_at TEXT NOT NULL,
                    last_played_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS play_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    spotify_id TEXT NOT NULL,
                    played_at TEXT NOT NULL,
                    listened_ms INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (spotify_id) REFERENCES tracks(spotify_id)
                );
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Bir şarkının dinlendiğini kaydeder veya günceller
        /// </summary>
        public void RecordPlay(TrackInfo track, int listenedMs)
        {
            if (_connection == null) return;

            var now = DateTime.UtcNow.ToString("o");

            // Önce track var mı kontrol et
            var checkCmd = _connection.CreateCommand();
            checkCmd.CommandText = "SELECT play_count, total_listened_ms FROM tracks WHERE spotify_id = @id";
            checkCmd.Parameters.AddWithValue("@id", track.Id);

            using var reader = checkCmd.ExecuteReader();
            if (reader.Read())
            {
                // Güncelle
                var currentCount = reader.GetInt32(0);
                var currentTotal = reader.GetInt64(1);
                reader.Close();

                var updateCmd = _connection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE tracks SET 
                        play_count = @count,
                        total_listened_ms = @total,
                        last_played_at = @now,
                        name = @name,
                        artist = @artist,
                        album = @album,
                        album_art_url = @art
                    WHERE spotify_id = @id";
                updateCmd.Parameters.AddWithValue("@count", currentCount + 1);
                updateCmd.Parameters.AddWithValue("@total", currentTotal + listenedMs);
                updateCmd.Parameters.AddWithValue("@now", now);
                updateCmd.Parameters.AddWithValue("@name", track.Name);
                updateCmd.Parameters.AddWithValue("@artist", track.Artist);
                updateCmd.Parameters.AddWithValue("@album", track.Album);
                updateCmd.Parameters.AddWithValue("@art", track.AlbumArtUrl);
                updateCmd.Parameters.AddWithValue("@id", track.Id);
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                reader.Close();

                // Yeni ekle
                var insertCmd = _connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO tracks (spotify_id, name, artist, album, album_art_url, duration_ms, play_count, total_listened_ms, first_played_at, last_played_at)
                    VALUES (@id, @name, @artist, @album, @art, @dur, 1, @listened, @now, @now)";
                insertCmd.Parameters.AddWithValue("@id", track.Id);
                insertCmd.Parameters.AddWithValue("@name", track.Name);
                insertCmd.Parameters.AddWithValue("@artist", track.Artist);
                insertCmd.Parameters.AddWithValue("@album", track.Album);
                insertCmd.Parameters.AddWithValue("@art", track.AlbumArtUrl);
                insertCmd.Parameters.AddWithValue("@dur", track.DurationMs);
                insertCmd.Parameters.AddWithValue("@listened", listenedMs);
                insertCmd.Parameters.AddWithValue("@now", now);
                insertCmd.ExecuteNonQuery();
            }

            // Play log'a da ekle
            var logCmd = _connection.CreateCommand();
            logCmd.CommandText = @"
                INSERT INTO play_log (spotify_id, played_at, listened_ms) 
                VALUES (@id, @now, @listened)";
            logCmd.Parameters.AddWithValue("@id", track.Id);
            logCmd.Parameters.AddWithValue("@now", now);
            logCmd.Parameters.AddWithValue("@listened", listenedMs);
            logCmd.ExecuteNonQuery();
        }

        /// <summary>
        /// En çok dinlenen şarkıları getirir
        /// </summary>
        public List<TrackStats> GetTopTracks(int limit = 20)
        {
            var tracks = new List<TrackStats>();
            if (_connection == null) return tracks;

            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT spotify_id, name, artist, album, album_art_url, duration_ms, 
                       play_count, total_listened_ms, first_played_at, last_played_at
                FROM tracks
                ORDER BY play_count DESC
                LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tracks.Add(new TrackStats
                {
                    SpotifyId = reader.GetString(0),
                    Name = reader.GetString(1),
                    Artist = reader.GetString(2),
                    Album = reader.GetString(3),
                    AlbumArtUrl = reader.GetString(4),
                    DurationMs = reader.GetInt32(5),
                    PlayCount = reader.GetInt32(6),
                    TotalListenedMs = reader.GetInt64(7),
                    FirstPlayedAt = DateTime.Parse(reader.GetString(8)),
                    LastPlayedAt = DateTime.Parse(reader.GetString(9))
                });
            }
            return tracks;
        }

        /// <summary>
        /// Genel istatistikleri getirir
        /// </summary>
        public OverallStats GetOverallStats()
        {
            var stats = new OverallStats();
            if (_connection == null) return stats;

            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    COUNT(*) as total_tracks,
                    COALESCE(SUM(play_count), 0) as total_plays,
                    COALESCE(SUM(total_listened_ms), 0) as total_listened
                FROM tracks";

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                stats.TotalTracks = reader.GetInt32(0);
                stats.TotalPlays = reader.GetInt32(1);
                stats.TotalListenedMs = reader.GetInt64(2);
            }
            reader.Close();

            // Bugünkü dinlemeler
            var todayCmd = _connection.CreateCommand();
            var todayStart = DateTime.UtcNow.Date.ToString("o");
            todayCmd.CommandText = @"
                SELECT COUNT(*) FROM play_log WHERE played_at >= @today";
            todayCmd.Parameters.AddWithValue("@today", todayStart);
            stats.TodayPlays = Convert.ToInt32(todayCmd.ExecuteScalar() ?? 0);

            return stats;
        }

        /// <summary>
        /// Tüm şarkıları getirir (çalma listesi oluşturma için)
        /// </summary>
        public List<TrackStats> GetAllTracks()
        {
            return GetTopTracks(1000);
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
