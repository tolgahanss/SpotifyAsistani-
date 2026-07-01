using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpotifyAsistani.Models;

namespace SpotifyAsistani.Services
{
    /// <summary>
    /// Spotify Web API servisi - OAuth ve API çağrıları
    /// </summary>
    public class SpotifyService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private SpotifyConfig _config;
        private HttpListener? _authListener;

        private const string AuthUrl = "https://accounts.spotify.com/authorize";
        private const string TokenUrl = "https://accounts.spotify.com/api/token";
        private const string ApiBaseUrl = "https://api.spotify.com/v1";
        private const string RedirectUri = "http://127.0.0.1:8391/callback";
        private const string Scopes = "user-read-currently-playing user-read-playback-state user-top-read playlist-read-private playlist-modify-public playlist-modify-private user-read-recently-played";

        public bool IsAuthenticated => !string.IsNullOrEmpty(_config.AccessToken) && DateTime.UtcNow < _config.TokenExpiry;
        public event Action<string>? OnStatusChanged;
        public event Action<string>? OnError;
        public event Action? OnConnected;

        public SpotifyService()
        {
            _httpClient = new HttpClient();
            _config = new SpotifyConfig();
        }

        #region Configuration

        public void SetCredentials(string clientId, string clientSecret)
        {
            _config.ClientId = clientId;
            _config.ClientSecret = clientSecret;
        }

        public void LoadConfig(SpotifyConfig config)
        {
            _config = config;
            if (IsAuthenticated)
            {
                OnConnected?.Invoke();
            }
        }

        public SpotifyConfig GetConfig() => _config;

        public bool HasCredentials => !string.IsNullOrEmpty(_config.ClientId) && !string.IsNullOrEmpty(_config.ClientSecret);

        #endregion

        #region OAuth Flow

        /// <summary>
        /// Spotify OAuth giriş URL'sini oluşturur ve tarayıcıda açar
        /// </summary>
        public async Task<bool> AuthenticateAsync()
        {
            if (!HasCredentials)
            {
                OnError?.Invoke("Lütfen önce Client ID ve Client Secret girin.");
                return false;
            }

            // Eğer mevcut token geçerliyse, refresh dene
            if (!string.IsNullOrEmpty(_config.RefreshToken))
            {
                var refreshed = await RefreshTokenAsync();
                if (refreshed) return true;
            }

            try
            {
                OnStatusChanged?.Invoke("Spotify'a bağlanılıyor...");

                _authListener = new HttpListener();
                _authListener.Prefixes.Add("http://127.0.0.1:8391/");
                _authListener.Start();

                // Tarayıcıyı aç
                var state = Guid.NewGuid().ToString("N");
                var authUri = $"{AuthUrl}?client_id={_config.ClientId}" +
                    $"&response_type=code" +
                    $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                    $"&scope={Uri.EscapeDataString(Scopes)}" +
                    $"&state={state}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = authUri,
                    UseShellExecute = true
                });

                var context = await _authListener.GetContextAsync();
                var code = context.Request.QueryString["code"];
                var returnedState = context.Request.QueryString["state"];

                // HTML yanıt gönder
                var responseHtml = @"
                    <html>
                    <body style='background:#0a0a0f;color:white;font-family:Inter,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;'>
                        <div style='text-align:center;'>
                            <h1 style='color:#1DB954;font-size:48px;'>✓</h1>
                            <h2>Spotify Bağlantısı Başarılı!</h2>
                            <p style='color:#a1a1aa;'>Bu pencereyi kapatabilirsiniz.</p>
                        </div>
                    </body>
                    </html>";
                var buffer = Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
                _authListener.Stop();

                if (string.IsNullOrEmpty(code) || returnedState != state)
                {
                    OnError?.Invoke("Yetkilendirme başarısız oldu.");
                    return false;
                }

                // Token al
                return await ExchangeCodeAsync(code);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Bağlantı hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ExchangeCodeAsync(string code)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", RedirectUri),
                });

                var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ClientId}:{_config.ClientSecret}"));
                var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
                request.Content = content;
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    OnError?.Invoke($"Token alınamadı: {json}");
                    return false;
                }

                var tokenData = JObject.Parse(json);
                _config.AccessToken = tokenData["access_token"]?.ToString() ?? "";
                _config.RefreshToken = tokenData["refresh_token"]?.ToString() ?? _config.RefreshToken;
                var expiresIn = tokenData["expires_in"]?.ToObject<int>() ?? 3600;
                _config.TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // 1 dk erken yenile


                OnStatusChanged?.Invoke("Spotify'a bağlandı! ✓");
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Token hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RefreshTokenAsync()
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", _config.RefreshToken),
                });

                var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ClientId}:{_config.ClientSecret}"));
                var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
                request.Content = content;
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                var tokenData = JObject.Parse(json);
                _config.AccessToken = tokenData["access_token"]?.ToString() ?? "";
                if (tokenData["refresh_token"] != null)
                    _config.RefreshToken = tokenData["refresh_token"]!.ToString();
                var expiresIn = tokenData["expires_in"]?.ToObject<int>() ?? 3600;
                _config.TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);


                OnStatusChanged?.Invoke("Token yenilendi ✓");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task EnsureTokenAsync()
        {
            if (DateTime.UtcNow >= _config.TokenExpiry && !string.IsNullOrEmpty(_config.RefreshToken))
            {
                await RefreshTokenAsync();
            }
        }

        #endregion

        #region API Calls

        /// <summary>
        /// Şu an çalan şarkıyı getirir
        /// </summary>
        public async Task<TrackInfo?> GetCurrentlyPlayingAsync()
        {
            await EnsureTokenAsync();
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AccessToken);

                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/me/player/currently-playing");

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null; // Hiçbir şey çalmıyor

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                if (data["item"] == null) return null;

                var item = data["item"]!;
                var artists = item["artists"]!;
                var artistNames = new List<string>();
                foreach (var a in artists)
                    artistNames.Add(a["name"]?.ToString() ?? "");

                var albumImages = item["album"]?["images"];
                var artUrl = "";
                if (albumImages != null && albumImages.HasValues)
                    artUrl = albumImages[0]?["url"]?.ToString() ?? "";

                return new TrackInfo
                {
                    Id = item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Artist = string.Join(", ", artistNames),
                    Album = item["album"]?["name"]?.ToString() ?? "",
                    AlbumArtUrl = artUrl,
                    DurationMs = item["duration_ms"]?.ToObject<int>() ?? 0,
                    ProgressMs = data["progress_ms"]?.ToObject<int>() ?? 0,
                    IsPlaying = data["is_playing"]?.ToObject<bool>() ?? false
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Kullanıcının çalma listelerini getirir
        /// </summary>
        public async Task<List<PlaylistInfo>> GetPlaylistsAsync()
        {
            await EnsureTokenAsync();
            var playlists = new List<PlaylistInfo>();
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AccessToken);

                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/me/playlists?limit=50");
                if (!response.IsSuccessStatusCode) return playlists;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                foreach (var item in data["items"]!)
                {
                    var images = item["images"];
                    var imgUrl = "";
                    if (images != null && images.HasValues)
                        imgUrl = images[0]?["url"]?.ToString() ?? "";

                    playlists.Add(new PlaylistInfo
                    {
                        Id = item["id"]?.ToString() ?? "",
                        Name = item["name"]?.ToString() ?? "",
                        Description = item["description"]?.ToString() ?? "",
                        ImageUrl = imgUrl,
                        TrackCount = item["tracks"]?["total"]?.ToObject<int>() ?? 0,
                        Owner = item["owner"]?["display_name"]?.ToString() ?? ""
                    });
                }
            }
            catch { }
            return playlists;
        }

        /// <summary>
        /// Belirtilen şarkılardan yeni bir çalma listesi oluşturur
        /// </summary>
        public async Task<bool> CreatePlaylistAsync(string name, string description, List<string> trackIds)
        {
            await EnsureTokenAsync();
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AccessToken);

                // Kullanıcı ID'sini al
                var meResponse = await _httpClient.GetAsync($"{ApiBaseUrl}/me");
                var meJson = await meResponse.Content.ReadAsStringAsync();
                var userId = JObject.Parse(meJson)["id"]?.ToString();
                if (string.IsNullOrEmpty(userId)) return false;

                // Çalma listesi oluştur
                var createPayload = JsonConvert.SerializeObject(new
                {
                    name,
                    description,
                    @public = false
                });

                var createResponse = await _httpClient.PostAsync(
                    $"{ApiBaseUrl}/users/{userId}/playlists",
                    new StringContent(createPayload, Encoding.UTF8, "application/json"));

                if (!createResponse.IsSuccessStatusCode) return false;

                var createJson = await createResponse.Content.ReadAsStringAsync();
                var playlistId = JObject.Parse(createJson)["id"]?.ToString();
                if (string.IsNullOrEmpty(playlistId)) return false;

                // Şarkıları ekle (100'lük gruplar halinde)
                var uris = new List<string>();
                foreach (var id in trackIds)
                    uris.Add($"spotify:track:{id}");

                for (int i = 0; i < uris.Count; i += 100)
                {
                    var batch = uris.GetRange(i, Math.Min(100, uris.Count - i));
                    var addPayload = JsonConvert.SerializeObject(new { uris = batch });
                    await _httpClient.PostAsync(
                        $"{ApiBaseUrl}/playlists/{playlistId}/tracks",
                        new StringContent(addPayload, Encoding.UTF8, "application/json"));
                }

                OnStatusChanged?.Invoke($"'{name}' çalma listesi oluşturuldu! ✓");
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Çalma listesi hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Bir çalma listesindeki tekrar eden şarkıları tespit eder
        /// </summary>
        public async Task<(List<string> duplicateNames, int count)> FindDuplicatesInPlaylistAsync(string playlistId)
        {
            await EnsureTokenAsync();
            var duplicateNames = new List<string>();
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AccessToken);

                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/playlists/{playlistId}/tracks?limit=100");
                if (!response.IsSuccessStatusCode) return (duplicateNames, 0);

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                var seen = new Dictionary<string, string>();
                var duplicates = new HashSet<string>();

                foreach (var item in data["items"]!)
                {
                    var track = item["track"];
                    if (track == null) continue;
                    var id = track["id"]?.ToString() ?? "";
                    var name = track["name"]?.ToString() ?? "";

                    if (seen.ContainsKey(id))
                    {
                        if (!duplicates.Contains(id))
                        {
                            duplicateNames.Add(name);
                            duplicates.Add(id);
                        }
                    }
                    else
                    {
                        seen[id] = name;
                    }
                }

                return (duplicateNames, duplicates.Count);
            }
            catch
            {
                return (duplicateNames, 0);
            }
        }

        /// <summary>
        /// Kullanıcı profil bilgisini getirir
        /// </summary>
        public async Task<(string name, string imageUrl)> GetUserProfileAsync()
        {
            await EnsureTokenAsync();
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AccessToken);

                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/me");
                if (!response.IsSuccessStatusCode) return ("", "");

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                var name = data["display_name"]?.ToString() ?? "";
                var images = data["images"];
                var imageUrl = "";
                if (images != null && images.HasValues)
                    imageUrl = images[0]?["url"]?.ToString() ?? "";

                return (name, imageUrl);
            }
            catch { return ("", ""); }
        }

        #endregion

        public void Dispose()
        {
            _authListener?.Close();
            _httpClient.Dispose();
        }
    }
}
