using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpotifyAsistani.Models;
using SpotifyAsistani.Services;

namespace SpotifyAsistani
{
    /// <summary>
    /// Spotify Asistanı Ana Pencere
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SpotifyService _spotify;
        private readonly DatabaseService _database;
        private readonly LyricsService _lyrics;
        private readonly DispatcherTimer _pollingTimer;
        private readonly DispatcherTimer _toastTimer;

        private TrackInfo? _currentTrack;
        private string _lastTrackId = "";
        private DateTime _lastTrackStartTime;
        private int _lastProgressMs;

        public MainWindow()
        {
            InitializeComponent();

            _spotify = new SpotifyService();
            _database = new DatabaseService();
            _lyrics = new LyricsService();

            // Spotify event'lerini bağla
            _spotify.OnStatusChanged += (msg) => Dispatcher.Invoke(() => ShowToast(msg, "✓"));
            _spotify.OnError += (msg) => Dispatcher.Invoke(() => ShowToast(msg, "⚠️"));

            // Polling timer: her 3 saniyede bir çalan şarkıyı kontrol et
            _pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _pollingTimer.Tick += PollingTimer_Tick;

            // Toast timer
            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _toastTimer.Tick += (s, e) =>
            {
                ToastPanel.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
        }

        #region Window Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Kayıtlı ayarları yükle
            var config = _spotify.GetConfig();
            if (!string.IsNullOrEmpty(config.ClientId))
            {
                TxtClientId.Text = config.ClientId;
                TxtClientSecret.Text = config.ClientSecret;
                SettingsClientId.Text = config.ClientId;
                SettingsClientSecret.Text = config.ClientSecret;
            }

            // Eğer zaten token varsa otomatik bağlan
            if (_spotify.IsAuthenticated)
            {
                OnConnected();
            }
            else if (_spotify.HasCredentials && !string.IsNullOrEmpty(config.RefreshToken))
            {
                // Refresh token ile bağlanmayı dene
                _ = TryAutoConnect();
            }

            // İstatistikleri güncelle
            RefreshStats();
            RefreshTopTracks();
        }

        private async System.Threading.Tasks.Task TryAutoConnect()
        {
            var result = await _spotify.AuthenticateAsync();
            if (result)
            {
                OnConnected();
            }
        }

        #endregion

        #region Navigation

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            // InitializeComponent sırasında elementler henüz hazır olmayabilir
            if (LoginPage == null || DashboardPage == null || StatsPage == null || PlaylistsPage == null || SettingsPage == null)
                return;

            // Tüm sayfaları gizle
            LoginPage.Visibility = Visibility.Collapsed;
            DashboardPage.Visibility = Visibility.Collapsed;
            StatsPage.Visibility = Visibility.Collapsed;
            PlaylistsPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;

            if (TabDashboard.IsChecked == true)
            {
                if (_spotify.IsAuthenticated)
                    DashboardPage.Visibility = Visibility.Visible;
                else
                    LoginPage.Visibility = Visibility.Visible;
            }
            else if (TabStats.IsChecked == true)
            {
                StatsPage.Visibility = Visibility.Visible;
                RefreshAllTracks();
            }
            else if (TabPlaylists.IsChecked == true)
            {
                PlaylistsPage.Visibility = Visibility.Visible;
                if (_spotify.IsAuthenticated)
                    _ = LoadPlaylists();
            }
            else if (TabSettings.IsChecked == true)
            {
                SettingsPage.Visibility = Visibility.Visible;
                var config = _spotify.GetConfig();
                SettingsClientId.Text = config.ClientId;
                SettingsClientSecret.Text = config.ClientSecret;
            }
        }

        #endregion

        #region Spotify Connection

        private async void ConnectSpotify_Click(object sender, RoutedEventArgs e)
        {
            var clientId = TxtClientId.Text.Trim();
            var clientSecret = TxtClientSecret.Text.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                ShowToast("Lütfen Client ID ve Client Secret girin.", "⚠️");
                return;
            }

            _spotify.SetCredentials(clientId, clientSecret);
            var result = await _spotify.AuthenticateAsync();

            if (result)
            {
                OnConnected();
            }
        }

        private void OnConnected()
        {
            Dispatcher.Invoke(() =>
            {
                // Login sayfasını gizle, dashboard'u göster
                LoginPage.Visibility = Visibility.Collapsed;
                DashboardPage.Visibility = Visibility.Visible;

                // Status güncelle
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
                StatusText.Text = "Bağlı";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));

                // Polling başlat
                _pollingTimer.Start();

                // İstatistikleri güncelle
                RefreshStats();
                RefreshTopTracks();
            });
        }

        #endregion

        #region Polling & Now Playing

        private async void PollingTimer_Tick(object? sender, EventArgs e)
        {
            if (!_spotify.IsAuthenticated) return;

            try
            {
                var track = await _spotify.GetCurrentlyPlayingAsync();

                Dispatcher.Invoke(() =>
                {
                    if (track != null && track.IsPlaying)
                    {
                        // Şarkı değişti mi kontrol et
                        if (track.Id != _lastTrackId)
                        {
                            // Önceki şarkıyı veritabanına kaydet
                            if (_currentTrack != null && !string.IsNullOrEmpty(_lastTrackId))
                            {
                                var listenedMs = (int)(DateTime.UtcNow - _lastTrackStartTime).TotalMilliseconds;
                                listenedMs = Math.Min(listenedMs, _currentTrack.DurationMs); // Max süreden fazla olmasın
                                _database.RecordPlay(_currentTrack, listenedMs);
                                RefreshStats();
                                RefreshTopTracks();
                            }

                            _lastTrackId = track.Id;
                            _lastTrackStartTime = DateTime.UtcNow;
                            _currentTrack = track;

                            // Şarkı sözlerini getir
                            _ = LoadLyrics(track);
                        }

                        _lastProgressMs = track.ProgressMs;

                        // UI güncelle
                        UpdateNowPlaying(track);
                    }
                    else
                    {
                        // Hiçbir şey çalmıyor - son çalan şarkıyı kaydet
                        if (_currentTrack != null && !string.IsNullOrEmpty(_lastTrackId))
                        {
                            var listenedMs = (int)(DateTime.UtcNow - _lastTrackStartTime).TotalMilliseconds;
                            listenedMs = Math.Min(listenedMs, _currentTrack.DurationMs);
                            if (listenedMs > 10000) // En az 10 saniye dinlenmişse kaydet
                            {
                                _database.RecordPlay(_currentTrack, listenedMs);
                                RefreshStats();
                                RefreshTopTracks();
                            }
                            _currentTrack = null;
                            _lastTrackId = "";
                        }

                        ShowNotPlaying();
                    }
                });
            }
            catch
            {
                // Hata durumunda sessizce devam et
            }
        }

        private void UpdateNowPlaying(TrackInfo track)
        {
            TrackTitle.Text = track.Name;
            TrackArtist.Text = track.Artist;
            TrackAlbum.Text = track.Album;
            ProgressTime.Text = track.ProgressFormatted;
            DurationTime.Text = track.DurationFormatted;

            // Progress bar
            var containerWidth = ProgressBar.Parent is System.Windows.Controls.Grid grid ? grid.ActualWidth : 300;
            ProgressBar.Width = containerWidth * (track.ProgressPercent / 100.0);

            // Album art
            if (!string.IsNullOrEmpty(track.AlbumArtUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(track.AlbumArtUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    AlbumArt.Source = bitmap;
                }
                catch { }
            }

            NowPlayingLabel.Text = "ŞU AN ÇALIYOR";
            NowPlayingDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
        }

        private void ShowNotPlaying()
        {
            TrackTitle.Text = "Çalan Şarkı Yok";
            TrackArtist.Text = "Spotify'da bir şarkı çalın";
            TrackAlbum.Text = "";
            ProgressTime.Text = "0:00";
            DurationTime.Text = "0:00";
            ProgressBar.Width = 0;
            NowPlayingLabel.Text = "DURAKLATILDI";
            NowPlayingDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#52525b"));
            LyricsText.Text = "Şarkı çalınırken sözler burada görünecek...";
        }

        #endregion

        #region Lyrics

        private async System.Threading.Tasks.Task LoadLyrics(TrackInfo track)
        {
            LyricsText.Text = "Şarkı sözleri aranıyor...";

            var lyrics = await _lyrics.GetLyricsAsync(track.Id, track.Artist, track.Name);

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(lyrics))
                {
                    LyricsText.Text = lyrics;
                }
                else
                {
                    LyricsText.Text = "😢 Bu şarkının sözleri bulunamadı.";
                }
            });
        }

        #endregion

        #region Stats

        private void RefreshStats()
        {
            var stats = _database.GetOverallStats();
            StatTotalTracks.Text = stats.TotalTracks.ToString();
            StatTotalPlays.Text = stats.TotalPlays.ToString();
            StatTotalTime.Text = stats.TotalListenedFormatted;
            StatTodayPlays.Text = stats.TodayPlays.ToString();
        }

        private void RefreshTopTracks()
        {
            var tracks = _database.GetTopTracks(15);
            var displayItems = new List<TrackDisplayItem>();

            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                displayItems.Add(new TrackDisplayItem
                {
                    Rank = (i + 1).ToString(),
                    RankColor = GetRankBrush(i),
                    Name = t.Name,
                    Artist = t.Artist,
                    AlbumArtUrl = t.AlbumArtUrl,
                    PlayCount = t.PlayCount.ToString(),
                    TotalListenedFormatted = t.TotalListenedFormatted,
                    LastPlayedFormatted = t.LastPlayedAt.ToLocalTime().ToString("dd MMM HH:mm")
                });
            }

            TopTracksList.ItemsSource = displayItems;
        }

        private void RefreshAllTracks()
        {
            var tracks = _database.GetAllTracks();
            var displayItems = new List<TrackDisplayItem>();

            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                displayItems.Add(new TrackDisplayItem
                {
                    Rank = (i + 1).ToString(),
                    RankColor = GetRankBrush(i),
                    Name = t.Name,
                    Artist = t.Artist,
                    AlbumArtUrl = t.AlbumArtUrl,
                    PlayCount = t.PlayCount.ToString(),
                    TotalListenedFormatted = t.TotalListenedFormatted,
                    LastPlayedFormatted = t.LastPlayedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm")
                });
            }

            AllTracksList.ItemsSource = displayItems;
        }

        private Brush GetRankBrush(int index)
        {
            return index switch
            {
                0 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")), // Altın
                1 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")), // Gümüş
                2 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CD7F32")), // Bronz
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#52525b"))
            };
        }

        #endregion

        #region Playlists

        private async System.Threading.Tasks.Task LoadPlaylists()
        {
            var playlists = await _spotify.GetPlaylistsAsync();
            Dispatcher.Invoke(() =>
            {
                PlaylistsGrid.ItemsSource = playlists;
            });
        }

        private async void CreateTopPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (!_spotify.IsAuthenticated)
            {
                ShowToast("Önce Spotify'a bağlanın.", "⚠️");
                return;
            }

            var topTracks = _database.GetTopTracks(50);
            if (topTracks.Count == 0)
            {
                ShowToast("Henüz dinleme verisi yok. Biraz müzik dinleyin!", "ℹ️");
                return;
            }

            var trackIds = topTracks.Select(t => t.SpotifyId).ToList();
            var date = DateTime.Now.ToString("dd MMM yyyy");
            var result = await _spotify.CreatePlaylistAsync(
                $"🏆 En Çok Dinlediklerim - {date}",
                $"Spotify Asistanı tarafından otomatik oluşturuldu. Top {topTracks.Count} şarkı.",
                trackIds);

            if (result)
            {
                ShowToast($"'{topTracks.Count}' şarkıdan çalma listesi oluşturuldu!", "✓");
                await LoadPlaylists();
            }
        }

        private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
        {
            if (!_spotify.IsAuthenticated)
            {
                ShowToast("Önce Spotify'a bağlanın.", "⚠️");
                return;
            }

            ShowToast("Çalma listeleri taranıyor...", "🔍");

            var playlists = await _spotify.GetPlaylistsAsync();
            int totalDuplicates = 0;
            var allDuplicateNames = new List<string>();

            foreach (var pl in playlists)
            {
                var (names, count) = await _spotify.FindDuplicatesInPlaylistAsync(pl.Id);
                totalDuplicates += count;
                allDuplicateNames.AddRange(names.Select(n => $"'{n}' ({pl.Name})"));
            }

            if (totalDuplicates > 0)
            {
                var message = $"Toplam {totalDuplicates} tekrar eden şarkı bulundu!\n\n" + string.Join("\n", allDuplicateNames.Take(10));
                if (allDuplicateNames.Count > 10)
                    message += $"\n... ve {allDuplicateNames.Count - 10} tane daha";
                MessageBox.Show(message, "Tekrar Eden Şarkılar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ShowToast("Hiç tekrar eden şarkı bulunamadı! ✓", "✓");
            }
        }

        private async void RefreshPlaylists_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify.IsAuthenticated)
            {
                await LoadPlaylists();
                ShowToast("Çalma listeleri güncellendi.", "✓");
            }
        }

        #endregion

        #region Settings

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _spotify.SetCredentials(SettingsClientId.Text.Trim(), SettingsClientSecret.Text.Trim());
            ShowToast("Ayarlar kaydedildi.", "✓");
        }

        private async void Reconnect_Click(object sender, RoutedEventArgs e)
        {
            _spotify.SetCredentials(SettingsClientId.Text.Trim(), SettingsClientSecret.Text.Trim());
            var result = await _spotify.AuthenticateAsync();
            if (result) OnConnected();
        }

        #endregion

        #region Toast Notifications

        private void ShowToast(string message, string icon = "ℹ️")
        {
            ToastIcon.Text = icon;
            ToastMessage.Text = message;
            ToastPanel.Visibility = Visibility.Visible;

            // Sol kenarlık rengini ayarla
            if (icon == "✓")
                ToastPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
            else if (icon == "⚠️")
                ToastPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f97316"));
            else
                ToastPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6"));

            _toastTimer.Stop();
            _toastTimer.Start();
        }

        #endregion

        #region Helpers

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Son çalan şarkıyı kaydet
            if (_currentTrack != null && !string.IsNullOrEmpty(_lastTrackId))
            {
                var listenedMs = (int)(DateTime.UtcNow - _lastTrackStartTime).TotalMilliseconds;
                listenedMs = Math.Min(listenedMs, _currentTrack.DurationMs);
                if (listenedMs > 5000)
                    _database.RecordPlay(_currentTrack, listenedMs);
            }

            _pollingTimer.Stop();
            _spotify.Dispose();
            _database.Dispose();
            _lyrics.Dispose();
            base.OnClosed(e);
        }

        #endregion
    }

    /// <summary>
    /// UI'da gösterilecek şarkı verisi (DataBinding için)
    /// </summary>
    public class TrackDisplayItem
    {
        public string Rank { get; set; } = "";
        public Brush RankColor { get; set; } = Brushes.Gray;
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
        public string AlbumArtUrl { get; set; } = "";
        public string PlayCount { get; set; } = "0";
        public string TotalListenedFormatted { get; set; } = "";
        public string LastPlayedFormatted { get; set; } = "";
    }
}