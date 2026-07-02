# Spotify Asistanı

Windows için geliştirilmiş, yerel veritabanı destekli, gelişmiş özelliklere sahip Spotify dinleme ve yönetim asistanı uygulaması.

![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-512BD4?style=for-the-badge&logo=windows&logoColor=white)
![Spotify API](https://img.shields.io/badge/Spotify_API-1DB954?style=for-the-badge&logo=spotify&logoColor=white)

---

## Özellikler

### 🎵 Canlı Oynatma İzleyicisi ve Kontrolü
- Spotify'da anlık olarak çalan şarkıyı uygulamadan takip etme.
- Şarkıyı Duraklatma, Oynatma, İleri/Geri Sarma.
- Çalan şarkıyı anında Favorilere (Beğenilen Şarkılar) ekleyip çıkarma (💚/🤍).
- Uygulama arka planda 3 saniyede bir Spotify'ı yoklar ve değişimleri anında algılar.

### 📜 Eş Zamanlı Şarkı Sözleri
- Çalan şarkının sözlerini anında internet üzerinden çeker (`LyricsService`) ve ekrana yansıtır.

### 📊 Yerel Dinleme Geçmişi ve İstatistikler
- Uygulama, çalan her şarkıyı ne kadar süre dinlediğinizi yerel veritabanına (`DatabaseService`) kaydeder.
- Spotify hesabınızın kendi kısıtlı "Geçmiş" özelliğine bağlı kalmadan yerel bir müzik arşivi tutar.
- **Top Tracks:** En çok dinlediğiniz ilk 15 (veya 50) şarkıyı listeler. (Altın, Gümüş, Bronz sıralama renkleriyle)
- Toplam şarkı sayısı, Toplam dinleme sayısı, Bugüne ait dinlemeler ve Toplam dinleme sürenizi görebilirsiniz.
- **Çevrimdışı Senkronizasyon:** Uygulama kapalıyken bile dinlediğiniz şarkıları, bir sonraki açılışta Spotify son dinlenenler (Recently Played) geçmişinden çeker ve dinleme toleranslarına (2 dk) göre yerel veritabanınızla senkronize eder.

### 🎧 Gelişmiş Çalma Listesi Yönetimi
- **Otomatik Top Listesi:** Yerel istatistiklerinize göre "🏆 En Çok Dinlediklerim" adlı Spotify çalma listesini tek tuşla otomatik olarak oluşturur.
- **Tekrar Eden Şarkı Tarayıcı (Duplicate Finder):** Tüm çalma listelerinizi tarayarak içinde aynı şarkıdan birden fazla kez bulunanları (duplicate) tespit eder ve size raporlar.

---

## Kurulum ve Ayarlar

Uygulamanın Spotify ile haberleşebilmesi için kendi Spotify Geliştirici (Developer) uygulamanızı oluşturmanız gerekmektedir.

1. [Spotify Developer Dashboard](https://developer.spotify.com/dashboard/)'a gidin ve giriş yapın.
2. Yeni bir uygulama oluşturun (Create App).
3. **Redirect URI** kısmına `http://localhost:5000/callback` (veya kodda tanımlı olan localhost port adresini) ekleyin.
4. Size verilen **Client ID** ve **Client Secret** kodlarını kopyalayın.
5. Uygulamayı çalıştırıp "Ayarlar" (Settings) veya "Giriş" (Login) ekranındaki ilgili kutucuklara bu bilgileri yapıştırın ve **Bağlan**'a tıklayın.
6. Tarayıcıda açılan izin penceresinden hesabınıza erişim izni verin. Uygulama otomatik olarak bağlanacaktır.

---

## Teknik Yapı ve Kod Mimarisi (`MainWindow.xaml.cs` Özeti)

Proje, WPF (Windows Presentation Foundation) mimarisine dayanmaktadır. Temel servisler şunlardır:

- **`SpotifyService.cs`**: Spotify Web API ile haberleşir (OAuth2 Auth, Play, Pause, Next, Recently Played, Playlist işlemleri).
- **`DatabaseService.cs`**: Uygulama içi yerel bir veritabanı oluşturulmasını ve tüm dinleme geçmişinin tutulmasını sağlar. Dinleme süreleri (ProgressMs) hesaplanarak veriler saklanır.
- **`LyricsService.cs`**: Çalan şarkının metadata verilerini alarak, sözlerini asenkron bir şekilde çeker.
- **UI Elementleri ve Polling (`DispatcherTimer`):** Belirli aralıklarla (3 saniye) arayüzde donma (UI blokajı) yaratmadan arka planda asenkron güncellemeler yapılır (`UpdateNowPlaying`, `PollingTimer_Tick`). Sayfalar arası geçişlerde (Navigation) `Visibility` durumları değiştirilerek akıcı bir deneyim sunulur.

---

## Lisans

Bu proje MIT Lisansı ile sunulmaktadır.
