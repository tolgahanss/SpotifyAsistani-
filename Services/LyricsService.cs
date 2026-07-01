using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpotifyAsistani.Services
{
    /// <summary>
    /// Şarkı sözlerini ücretsiz API'lerden çeker
    /// </summary>
    public class LyricsService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string _cachedTrackId = "";
        private string _cachedLyrics = "";

        public LyricsService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Şarkı sözlerini getirir (önbellekli)
        /// </summary>
        public async Task<string> GetLyricsAsync(string trackId, string artist, string title)
        {
            // Önbellekte varsa döndür
            if (trackId == _cachedTrackId && !string.IsNullOrEmpty(_cachedLyrics))
                return _cachedLyrics;

            _cachedTrackId = trackId;

            // Sanatçı ve şarkı adını temizle
            var cleanArtist = CleanName(artist);
            var cleanTitle = CleanTitle(title);

            // lyrics.ovh API'sini dene
            var lyrics = await TryLyricsOvh(cleanArtist, cleanTitle);

            if (string.IsNullOrEmpty(lyrics))
            {
                // Alternatif: lrclib.net API'sini dene
                lyrics = await TryLrcLib(cleanArtist, cleanTitle);
            }

            _cachedLyrics = lyrics ?? "";
            return _cachedLyrics;
        }

        private async Task<string?> TryLyricsOvh(string artist, string title)
        {
            try
            {
                var url = $"https://api.lyrics.ovh/v1/{Uri.EscapeDataString(artist)}/{Uri.EscapeDataString(title)}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                var lyrics = data["lyrics"]?.ToString()?.Trim();
                return string.IsNullOrEmpty(lyrics) ? null : lyrics;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> TryLrcLib(string artist, string title)
        {
            try
            {
                var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "SpotifyAsistani/1.0");
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                // Senkronize sözler varsa onları, yoksa düz sözleri kullan
                var syncedLyrics = data["syncedLyrics"]?.ToString();
                var plainLyrics = data["plainLyrics"]?.ToString();

                if (!string.IsNullOrEmpty(plainLyrics))
                    return plainLyrics.Trim();
                if (!string.IsNullOrEmpty(syncedLyrics))
                {
                    // Zaman damgalarını temizle
                    return System.Text.RegularExpressions.Regex.Replace(
                        syncedLyrics, @"\[\d{2}:\d{2}\.\d{2,3}\]\s*", "").Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string CleanName(string name)
        {
            // İlk sanatçıyı al (virgülle ayrılmışsa)
            var idx = name.IndexOf(',');
            if (idx > 0) name = name.Substring(0, idx);
            return name.Trim();
        }

        private string CleanTitle(string title)
        {
            // Parantez içindeki kısımları temizle (feat., remix vb.)
            title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*[\(\[].*?[\)\]]", "");
            title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*-\s*(feat|ft)\.?.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return title.Trim();
        }

        public void ClearCache()
        {
            _cachedTrackId = "";
            _cachedLyrics = "";
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
