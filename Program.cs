using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace M3U8Extractor
{
    // Data Models für hierarchische Struktur
    public class Episode : INotifyPropertyChanged
    {
        public int Number { get; set; }
        public string M3U8Url { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime? FoundAt { get; set; }
        public EpisodeStatus Status { get; set; } = EpisodeStatus.Pending;
        public string ErrorMessage { get; set; } = "";

        public event PropertyChangedEventHandler PropertyChanged;

        public void UpdateStatus(EpisodeStatus status, string message = "")
        {
            Status = status;
            ErrorMessage = message;
            if (status == EpisodeStatus.Found)
                FoundAt = DateTime.Now;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public class Season : INotifyPropertyChanged
    {
        public int Number { get; set; }
        public ObservableCollection<Episode> Episodes { get; set; } = new ObservableCollection<Episode>();
        public int TotalEpisodes => Episodes.Count;
        public int FoundEpisodes => Episodes.Count(e => e.Status == EpisodeStatus.Found);
        public int ErrorEpisodes => Episodes.Count(e => e.Status == EpisodeStatus.Error);

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyStatsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FoundEpisodes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorEpisodes)));
        }
    }

    public class Series : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string CleanName { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public ObservableCollection<Season> Seasons { get; set; } = new ObservableCollection<Season>();
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }

        public int TotalEpisodes => Seasons.Sum(s => s.TotalEpisodes);
        public int FoundEpisodes => Seasons.Sum(s => s.FoundEpisodes);
        public int ErrorEpisodes => Seasons.Sum(s => s.ErrorEpisodes);

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyStatsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalEpisodes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FoundEpisodes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorEpisodes)));
        }
    }

    public enum EpisodeStatus
    {
        Pending,
        Processing,
        Found,
        Error,
        Skipped
    }

    // Main Extractor Class
    class Program
    {
        private static ChromeDriver driver;
        private static Series currentSeries;
        private static Season currentSeasonObj;
        private static Episode currentEpisodeObj;

        private static int errorCount = 0;
        private static int maxErrors = 10;
        private static string tempProfilePath = "";

        // Live Statistics
        private static readonly object statsLock = new object();
        private static DateTime lastStatsUpdate = DateTime.Now;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== M3U8 URL Extractor - Collection Based ===");
            Console.WriteLine();

            // User-Eingabe für die Seite
            Console.Write("Geben Sie die URL der ersten Episode ein: ");
            var userInputUrl = Console.ReadLine();

            if (string.IsNullOrEmpty(userInputUrl))
            {
                Console.WriteLine("Keine URL eingegeben. Programm wird beendet.");
                return;
            }

            // Initialisiere Series Collection
            currentSeries = new Series
            {
                BaseUrl = userInputUrl,
                Name = ExtractSeriesNameFromUrl(userInputUrl),
                CleanName = CleanFileName(ExtractSeriesNameFromUrl(userInputUrl))
            };

            Console.WriteLine($"🎬 Serie: {currentSeries.Name}");
            Console.WriteLine($"🎯 Basis-URL: {userInputUrl}");
            Console.WriteLine("Drücken Sie ENTER zum Starten...");
            Console.ReadLine();

            // Setup Browser
            SetupTempProfile();
            driver = CreateChromeDriver();

            try
            {
                // Setup Network Monitoring
                SetupNetworkMonitoring();

                // Starte Extraktion
                await ExtractSeriesData(userInputUrl);
            }
            catch (Exception ex)
            {
                LogError($"Kritischer Fehler: {ex.Message}");
            }
            finally
            {
                driver?.Quit();
                CleanupTempProfile(tempProfilePath);

                // Export Results
                await ExportResults();
                ShowFinalResults();
            }
        }

        private static async Task ExtractSeriesData(string startUrl)
        {
            // Navigiere zur ersten Episode
            driver.Navigate().GoToUrl(startUrl);
            await WaitForPageLoad();

            var startingSeason = GetSeasonFromUrl(startUrl);
            var startingEpisode = GetEpisodeFromUrl(startUrl);

            Console.WriteLine($"📍 Starte bei Staffel {startingSeason}, Episode {startingEpisode}");

            // Initialisiere erste Staffel
            currentSeasonObj = GetOrCreateSeason(startingSeason);

            int currentSeasonNumber = startingSeason;
            int currentEpisodeNumber = startingEpisode;
            int consecutiveErrors = 0;
            const int maxConsecutiveErrors = 5;

            while (errorCount < maxErrors && consecutiveErrors < maxConsecutiveErrors)
            {
                try
                {
                    // Prüfe ob wir in neuer Staffel sind
                    var detectedSeason = GetSeasonFromUrl(driver.Url);
                    var detectedEpisode = GetEpisodeFromUrl(driver.Url);

                    if (detectedSeason != currentSeasonNumber)
                    {
                        // Neue Staffel erkannt
                        Console.WriteLine($"\n🎬 Staffel {currentSeasonNumber} → Staffel {detectedSeason}");

                        // Frage User ob weitermachen
                        Console.WriteLine($"❓ Soll Staffel {detectedSeason} verarbeitet werden? (j/n): ");
                        var choice = Console.ReadKey().KeyChar;
                        Console.WriteLine();

                        if (choice == 'j' || choice == 'J' || choice == 'y' || choice == 'Y')
                        {
                            currentSeasonNumber = detectedSeason;
                            currentEpisodeNumber = detectedEpisode;
                            currentSeasonObj = GetOrCreateSeason(currentSeasonNumber);
                            Console.WriteLine($"🚀 Starte Staffel {currentSeasonNumber}");
                        }
                        else
                        {
                            Console.WriteLine("🛑 Verarbeitung beendet auf User-Wunsch");
                            break;
                        }
                    }

                    // Korrigiere Episode-Nummer falls nötig
                    if (detectedEpisode > 0 && detectedEpisode != currentEpisodeNumber)
                    {
                        currentEpisodeNumber = detectedEpisode;
                    }

                    // Erstelle/Update Episode in Collection
                    currentEpisodeObj = GetOrCreateEpisode(currentSeasonObj, currentEpisodeNumber);
                    currentEpisodeObj.UpdateStatus(EpisodeStatus.Processing);

                    Console.WriteLine($"\n📺 Verarbeite Staffel {currentSeasonNumber}, Episode {currentEpisodeNumber}...");
                    PrintLiveStats();

                    // Versuche M3U8 URL zu finden
                    bool success = await ProcessCurrentEpisode();

                    if (success)
                    {
                        currentEpisodeObj.UpdateStatus(EpisodeStatus.Found);
                        consecutiveErrors = 0;
                        Console.WriteLine($"✅ Episode {currentEpisodeNumber} erfolgreich!");
                    }
                    else
                    {
                        currentEpisodeObj.UpdateStatus(EpisodeStatus.Error, "Keine M3U8 URL gefunden");
                        consecutiveErrors++;
                        LogError($"Episode {currentEpisodeNumber} fehlgeschlagen");
                    }

                    // Navigiere zur nächsten Episode
                    bool hasNext = await NavigateToNextEpisode();
                    if (!hasNext)
                    {
                        Console.WriteLine($"🔍 Keine weitere Episode gefunden");

                        // Versuche nächste Staffel
                        bool hasNextSeason = await TryNavigateToNextSeason(currentSeasonNumber);
                        if (!hasNextSeason)
                        {
                            Console.WriteLine("🏁 Keine weiteren Staffeln - Extraktion beendet");
                            break;
                        }
                        else
                        {
                            continue; // URL wird im nächsten Loop aktualisiert
                        }
                    }

                    currentEpisodeNumber++;

                    // Kurze Pause zwischen Episoden
                    await Task.Delay(300); // OPTIMIERT: 0.3s statt 0.5s

                    // Update Statistics
                    currentSeasonObj.NotifyStatsChanged();
                    currentSeries.NotifyStatsChanged();
                }
                catch (Exception ex)
                {
                    LogError($"Fehler bei Episode {currentEpisodeNumber}: {ex.Message}");
                    currentEpisodeObj?.UpdateStatus(EpisodeStatus.Error, ex.Message);
                    consecutiveErrors++;
                    await Task.Delay(2000);
                }
            }

            currentSeries.CompletedAt = DateTime.Now;
            Console.WriteLine($"\n🎉 Extraktion abgeschlossen!");
        }

        private static async Task<bool> ProcessCurrentEpisode()
        {
            await WaitForPageLoad();

            // Versuche Stream-Links zu aktivieren
            bool streamFound = await TryActivateStreams();
            if (!streamFound)
            {
                Console.WriteLine("❌ Keine Stream-Links gefunden");
                return false;
            }

            // Prüfe auf M3U8 URLs (erste Prüfung)
            await Task.Delay(1500); // OPTIMIERT: 1.5s statt 2s
            var foundUrls = CheckForM3U8URLs();

            Console.WriteLine($"🔍 Erste M3U8 Prüfung: {foundUrls.Count} URLs gefunden");

            if (foundUrls.Count == 0)
            {
                // Zweiter Versuch nach kurzer Wartezeit
                Console.WriteLine("🔄 Zweite M3U8 Prüfung...");
                await Task.Delay(1000); // OPTIMIERT: 1s statt 1.5s
                foundUrls = CheckForM3U8URLs();
                Console.WriteLine($"🔍 Zweite M3U8 Prüfung: {foundUrls.Count} URLs gefunden");
            }

            if (foundUrls.Count > 0)
            {
                // Debug: Zeige alle gefundenen URLs
                Console.WriteLine($"📋 Gefundene M3U8 URLs:");
                foreach (var url in foundUrls)
                {
                    Console.WriteLine($"   🔗 {url.Substring(0, Math.Min(80, url.Length))}...");
                }

                // Beste URL auswählen (bevorzuge master.m3u8)
                var masterUrls = foundUrls.Where(url => url.Contains("master.m3u8")).ToList();
                var bestUrl = masterUrls.FirstOrDefault() ?? foundUrls[0];

                if (masterUrls.Count > 0)
                {
                    Console.WriteLine($"🎯 Master.m3u8 URL gewählt");
                }
                else
                {
                    Console.WriteLine($"⚠️  Keine master.m3u8 gefunden - nehme erste verfügbare URL");
                }

                currentEpisodeObj.M3U8Url = bestUrl;
                currentEpisodeObj.Title = GetEpisodeTitle();

                Console.WriteLine($"✅ M3U8 URL gespeichert: {bestUrl.Substring(0, Math.Min(60, bestUrl.Length))}...");
                return true;
            }
            else
            {
                Console.WriteLine("❌ Keine M3U8 URLs gefunden");
                return false;
            }
        }

        private static async Task<bool> TryActivateStreams()
        {
            var streamSelectors = new[]
            {
                ".watchEpisode",
                ".hosterSiteVideoButton",
                ".generateInlinePlayer a",
                "a[href*='redirect']",
                "li[data-link-target] a"
            };

            foreach (var selector in streamSelectors)
            {
                try
                {
                    var elements = driver.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0)
                    {
                        Console.WriteLine($"🔍 {elements.Count} Stream-Links gefunden ({selector})");

                        // Klicke auf ersten Link
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", elements[0]);
                        await Task.Delay(1500);

                        // Versuche Redirect zu folgen
                        bool success = await FollowRedirectsToVideoPage();
                        if (success)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Stream-Aktivierung Fehler: {ex.Message}");
                }
            }

            return false;
        }

        private static async Task<bool> FollowRedirectsToVideoPage()
        {
            var originalUrl = driver.Url;
            var foundVideoUrls = new List<string>(); // WICHTIG: Sammle URLs bevor wir zurücknavigieren

            try
            {
                await Task.Delay(1500); // OPTIMIERT: 1.5s statt 2s

                var iframes = driver.FindElements(By.TagName("iframe"));
                foreach (var iframe in iframes)
                {
                    var src = iframe.GetAttribute("src");
                    if (!string.IsNullOrEmpty(src) && (src.Contains("redirect") || src.Contains("/redirect/")))
                    {
                        Console.WriteLine($"🔗 Folge Redirect: {src}");

                        string fullUrl = src.StartsWith("/")
                            ? $"{new Uri(driver.Url).Scheme}://{new Uri(driver.Url).Host}{src}"
                            : src;

                        driver.Navigate().GoToUrl(fullUrl);
                        await WaitForPageLoad();
                        await Task.Delay(1500); // OPTIMIERT: 1.5s statt 2s

                        SetupVideoPageNetworkMonitoring();

                        // Versuche Play-Buttons und sammle URLs
                        bool buttonSuccess = await TryPlayButtonsOnVideoPage();

                        // WICHTIG: Sammle URLs BEVOR wir zurücknavigieren
                        var videoPageUrls = CheckForM3U8URLs();
                        foreach (var url in videoPageUrls)
                        {
                            if (!foundVideoUrls.Contains(url))
                            {
                                foundVideoUrls.Add(url);
                                Console.WriteLine($"💾 URL aus Video-Seite gespeichert: {url.Substring(0, Math.Min(60, url.Length))}...");
                            }
                        }

                        // Zurück zur ursprünglichen Seite
                        driver.Navigate().GoToUrl(originalUrl);
                        await WaitForPageLoad();

                        // WICHTIG: Füge URLs zur globalen Liste hinzu (für CheckForM3U8URLs später)
                        if (foundVideoUrls.Count > 0)
                        {
                            var addUrlsScript = "window.foundM3U8URLs = window.foundM3U8URLs || []; ";
                            foreach (var url in foundVideoUrls)
                            {
                                addUrlsScript += $"window.foundM3U8URLs.push('{url}'); ";
                            }
                            ((IJavaScriptExecutor)driver).ExecuteScript(addUrlsScript);

                            Console.WriteLine($"✅ {foundVideoUrls.Count} M3U8 URLs zur Hauptseite transferiert");
                            return true;
                        }
                    }
                }

                return foundVideoUrls.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Redirect-Fehler: {ex.Message}");
                try
                {
                    driver.Navigate().GoToUrl(originalUrl);
                    await WaitForPageLoad();
                }
                catch { }
                return foundVideoUrls.Count > 0;
            }
        }

        private static async Task<bool> TryPlayButtonsOnVideoPage()
        {
            var playButtonSelectors = new[]
            {
                ".vjs-big-play-button",
                ".jw-display-icon-container",
                "button[aria-label*='play']",
                ".play-button",
                "video"
            };

            foreach (var selector in playButtonSelectors)
            {
                try
                {
                    var elements = driver.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0)
                    {
                        var element = elements[0];
                        if (element.Displayed && element.Enabled)
                        {
                            Console.WriteLine($"🎮 Klicke Play-Button: {selector}");
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);
                            await Task.Delay(1000); // OPTIMIERT: 1s statt 1.5s

                            var urls = CheckForM3U8URLs();
                            if (urls.Count > 0)
                            {
                                Console.WriteLine($"✅ M3U8 auf Video-Seite gefunden: {urls.Count} URLs");
                                foreach (var url in urls)
                                {
                                    Console.WriteLine($"   🔗 {url.Substring(0, Math.Min(60, url.Length))}...");
                                }
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Play-Button Fehler: {ex.Message}");
                }
            }

            return false;
        }

        private static List<string> CheckForM3U8URLs()
        {
            var foundUrls = new List<string>();

            try
            {
                // JavaScript URLs prüfen
                var jsUrls = ((IJavaScriptExecutor)driver).ExecuteScript("return window.foundM3U8URLs || [];")
                    as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                if (jsUrls != null)
                {
                    foreach (var urlObj in jsUrls)
                    {
                        var url = urlObj?.ToString();
                        if (IsValidM3U8Url(url))
                        {
                            foundUrls.Add(url);
                        }
                    }
                }

                // Performance API als Backup
                var script = @"
                    try {
                        var entries = performance.getEntriesByType('resource');
                        var m3u8Urls = [];
                        entries.forEach(function(entry) {
                            if (entry.name.includes('.m3u8') && !entry.name.includes('.ts')) {
                                m3u8Urls.push(entry.name);
                            }
                        });
                        return m3u8Urls;
                    } catch(e) {
                        return [];
                    }
                ";

                var performanceUrls = ((IJavaScriptExecutor)driver).ExecuteScript(script)
                    as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                if (performanceUrls != null)
                {
                    foreach (var urlObj in performanceUrls)
                    {
                        var url = urlObj?.ToString();
                        if (IsValidM3U8Url(url) && !foundUrls.Contains(url))
                        {
                            foundUrls.Add(url);
                        }
                    }
                }

                // Reset Browser URLs
                ((IJavaScriptExecutor)driver).ExecuteScript("window.foundM3U8URLs = [];");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  M3U8 Check Fehler: {ex.Message}");
            }

            return foundUrls;
        }

        // Collection Management
        private static Season GetOrCreateSeason(int seasonNumber)
        {
            var season = currentSeries.Seasons.FirstOrDefault(s => s.Number == seasonNumber);
            if (season == null)
            {
                season = new Season { Number = seasonNumber };
                currentSeries.Seasons.Add(season);
                Console.WriteLine($"📁 Neue Staffel erstellt: {seasonNumber}");
            }
            return season;
        }

        private static Episode GetOrCreateEpisode(Season season, int episodeNumber)
        {
            var episode = season.Episodes.FirstOrDefault(e => e.Number == episodeNumber);
            if (episode == null)
            {
                episode = new Episode { Number = episodeNumber };
                season.Episodes.Add(episode);
            }
            return episode;
        }

        // Export Functions
        private static async Task ExportResults()
        {
            try
            {
                // JSON Export
                await ExportToJson();

                // Batch Export (für yt-dlp)
                await ExportToBatch();

                // CSV Export
                await ExportToCsv();

                Console.WriteLine("📄 Alle Export-Dateien erstellt!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Export-Fehler: {ex.Message}");
            }
        }

        private static async Task ExportToJson()
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(currentSeries, jsonOptions);

            var fileName = $"{currentSeries.CleanName}_extraction.json";
            await File.WriteAllTextAsync(fileName, json);
            Console.WriteLine($"💾 JSON Export: {fileName}");
        }

        private static async Task ExportToBatch()
        {
            foreach (var season in currentSeries.Seasons)
            {
                var foundEpisodes = season.Episodes.Where(e => e.Status == EpisodeStatus.Found).ToList();
                if (foundEpisodes.Count == 0) continue;

                var fileName = $"{currentSeries.CleanName}_S{season.Number:D2}_yt-dlp.bat";
                var batchContent = GenerateBatchContent(season, foundEpisodes);

                await File.WriteAllTextAsync(fileName, batchContent);
                Console.WriteLine($"🎬 Batch Export: {fileName} ({foundEpisodes.Count} Episodes)");
            }
        }

        private static async Task ExportToCsv()
        {
            var csv = new List<string> { "Serie,Staffel,Episode,Status,M3U8_URL,Titel,Gefunden_Am" };

            foreach (var season in currentSeries.Seasons)
            {
                foreach (var episode in season.Episodes)
                {
                    csv.Add($"{currentSeries.Name},{season.Number},{episode.Number},{episode.Status}," +
                           $"\"{episode.M3U8Url}\",\"{episode.Title}\",{episode.FoundAt?.ToString("yyyy-MM-dd HH:mm:ss")}");
                }
            }

            var fileName = $"{currentSeries.CleanName}_episodes.csv";
            await File.WriteAllLinesAsync(fileName, csv);
            Console.WriteLine($"📊 CSV Export: {fileName}");
        }

        private static string GenerateBatchContent(Season season, List<Episode> episodes)
        {
            var header = $@"@echo off
setlocal enabledelayedexpansion
REM ===== yt-dlp Download System =====
REM Serie: {currentSeries.Name}
REM Staffel: {season.Number}
REM Generiert am: {DateTime.Now}
REM Episoden: {episodes.Count}

if not exist ""Downloads"" mkdir Downloads
cd Downloads

set /a running_downloads=0
set /a max_parallel=2

echo.
echo 🚀 Starte Download-System für {currentSeries.Name} Staffel {season.Number}
echo ================================================
echo.

goto :start_downloads

:download_episode
set ""url=%~1""
set ""final_name=%~2""

:wait_for_slot
if !running_downloads! geq %max_parallel% (
    timeout /t 5 /nobreak >nul
    goto :wait_for_slot
)

set /a running_downloads+=1
echo [!time!] 📥 Start: %final_name% (Slot !running_downloads!/%max_parallel%)

start /b """" cmd /c ""call :do_download ""%url%"" ""%final_name%""&&set /a running_downloads-=1""
goto :eof

:do_download
set ""url=%~1""
set ""final_name=%~2""

echo [%time%] 🔄 Downloading: %final_name%
yt-dlp ""%url%"" --output ""%final_name%%.%%(ext)s"" --merge-output-format mp4 --embed-metadata --no-playlist

if %errorlevel% equ 0 (
    echo [%time%] ✅ Completed: %final_name%
) else (
    echo [%time%] ❌ Failed: %final_name%
)

set /a running_downloads-=1
goto :eof

:start_downloads
REM Download-Commands:

";

            var commands = new List<string>();
            foreach (var episode in episodes.OrderBy(e => e.Number))
            {
                var fileName = $"{currentSeries.CleanName}S{season.Number:D2}F{episode.Number:D2}";
                commands.Add($"call :download_episode \"{episode.M3U8Url}\" \"{fileName}\"");
            }

            var footer = $@"
:wait_for_completion
if !running_downloads! gtr 0 (
    echo [%time%] ⏳ Warte auf %running_downloads% laufende Downloads...
    timeout /t 10 /nobreak >nul
    goto :wait_for_completion
)

echo.
echo ================================================
echo 🎉 Alle Downloads abgeschlossen!
echo.
echo 📊 Download-Statistik:
echo    - Serie: {currentSeries.Name}
echo    - Staffel: {season.Number}
echo    - Episoden: {episodes.Count}
echo    - Zielordner: %cd%
echo.

dir /b *.mp4 2>nul && echo. || echo Keine .mp4 Dateien gefunden
echo ✨ Fertig! Drücken Sie eine Taste zum Beenden...
pause >nul
";

            return header + string.Join("\n", commands) + footer;
        }

        // Utility Functions
        private static void PrintLiveStats()
        {
            lock (statsLock)
            {
                if (DateTime.Now - lastStatsUpdate < TimeSpan.FromSeconds(2)) return;
                lastStatsUpdate = DateTime.Now;

                Console.WriteLine($"📊 Live-Stats: {currentSeries.FoundEpisodes}/{currentSeries.TotalEpisodes} gefunden, " +
                                $"{currentSeries.ErrorEpisodes} Fehler, " +
                                $"Laufzeit: {DateTime.Now - currentSeries.StartedAt:mm\\:ss}");
            }
        }

        private static void SetupNetworkMonitoring()
        {
            var script = @"
                window.foundM3U8URLs = [];
                
                if (window.fetch) {
                    const originalFetch = window.fetch;
                    window.fetch = function(...args) {
                        const url = args[0];
                        if (typeof url === 'string' && url.includes('.m3u8')) {
                            window.foundM3U8URLs.push(url);
                        }
                        return originalFetch.apply(this, args);
                    };
                }
                
                const originalOpen = XMLHttpRequest.prototype.open;
                XMLHttpRequest.prototype.open = function(method, url, ...args) {
                    if (typeof url === 'string' && url.includes('.m3u8')) {
                        window.foundM3U8URLs.push(url);
                    }
                    return originalOpen.apply(this, [method, url, ...args]);
                };
            ";

            ((IJavaScriptExecutor)driver).ExecuteScript(script);
            Console.WriteLine("📡 Netzwerk-Monitoring aktiviert");
        }

        private static void SetupVideoPageNetworkMonitoring()
        {
            var script = @"
                window.foundM3U8URLs = window.foundM3U8URLs || [];
                
                if (window.fetch) {
                    const originalFetch = window.fetch;
                    window.fetch = function(...args) {
                        const url = args[0];
                        if (typeof url === 'string' && url.includes('.m3u8')) {
                            window.foundM3U8URLs.push(url);
                            console.log('*** M3U8 found on video page:', url);
                        }
                        return originalFetch.apply(this, args);
                    };
                }
            ";

            ((IJavaScriptExecutor)driver).ExecuteScript(script);
        }

        private static bool IsValidM3U8Url(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!url.Contains(".m3u8")) return false;
            if (url.Contains("analytics") || url.Contains("tracking")) return false;
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) return false;
            return true;
        }

        private static string ExtractSeriesNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments;

                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i] == "stream/" && i + 1 < segments.Length)
                    {
                        var rawName = segments[i + 1].TrimEnd('/').Replace("-", " ");
                        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(rawName);
                    }
                }

                return "Unknown Series";
            }
            catch
            {
                return "Unknown Series";
            }
        }

        private static string CleanFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private static int GetSeasonFromUrl(string url)
        {
            try
            {
                var match = Regex.Match(url, @"/staffel-(\d+)/", RegexOptions.IgnoreCase);
                return match.Success && int.TryParse(match.Groups[1].Value, out int season) ? season : 1;
            }
            catch { return 1; }
        }

        private static int GetEpisodeFromUrl(string url)
        {
            try
            {
                var match = Regex.Match(url, @"/episode-(\d+)", RegexOptions.IgnoreCase);
                return match.Success && int.TryParse(match.Groups[1].Value, out int episode) ? episode : 1;
            }
            catch { return 1; }
        }

        private static string GetEpisodeTitle()
        {
            try
            {
                var title = driver.Title;
                var parts = title.Split(new[] { " | ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[0].Trim() : "";
            }
            catch { return ""; }
        }

        // Navigation
        private static async Task<bool> NavigateToNextEpisode()
        {
            try
            {
                var originalUrl = driver.Url;
                var episodeLinks = driver.FindElements(By.CssSelector("a[data-episode-id]"));

                if (episodeLinks.Count > 0)
                {
                    var activeLink = episodeLinks.FirstOrDefault(link =>
                        link.GetAttribute("class")?.Contains("active") == true);

                    if (activeLink != null)
                    {
                        var activeIndex = episodeLinks.ToList().IndexOf(activeLink);
                        if (activeIndex + 1 < episodeLinks.Count)
                        {
                            var nextLink = episodeLinks[activeIndex + 1];
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", nextLink);
                            await WaitForPageLoad();
                            return driver.Url != originalUrl;
                        }
                    }
                }

                // Fallback: URL-basierte Navigation
                var currentEpisode = GetEpisodeFromUrl(driver.Url);
                var nextUrl = driver.Url.Replace($"episode-{currentEpisode}", $"episode-{currentEpisode + 1}");

                if (nextUrl != driver.Url)
                {
                    driver.Navigate().GoToUrl(nextUrl);
                    await WaitForPageLoad();

                    var title = driver.Title.ToLower();
                    return !title.Contains("404") && !title.Contains("not found");
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Navigation Fehler: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> TryNavigateToNextSeason(int currentSeason)
        {
            try
            {
                var nextSeason = currentSeason + 1;
                var currentUrl = driver.Url;
                var nextSeasonUrl = currentUrl.Replace($"staffel-{currentSeason}", $"staffel-{nextSeason}")
                                             .Replace($"episode-", "episode-1");

                driver.Navigate().GoToUrl(nextSeasonUrl);
                await WaitForPageLoad();

                var title = driver.Title.ToLower();
                return !title.Contains("404") && !title.Contains("not found");
            }
            catch
            {
                return false;
            }
        }

        // Browser Management
        private static ChromeDriver CreateChromeDriver()
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            options.AddArgument("--disable-web-security");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-logging");
            options.AddArgument("--log-level=3");
            options.AddArgument("--silent");

            if (!string.IsNullOrEmpty(tempProfilePath))
            {
                options.AddArgument($"--user-data-dir={tempProfilePath}");
            }

            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            return new ChromeDriver(service, options);
        }

        private static async Task<bool> WaitForPageLoad()
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(6)); // OPTIMIERT: 6s statt 8s
                wait.Until(driver => ((IJavaScriptExecutor)driver)
                    .ExecuteScript("return document.readyState").Equals("complete"));
                await Task.Delay(600); // OPTIMIERT: 0.6s statt 0.8s
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Page Load Timeout: {ex.Message}");
                return false;
            }
        }

        private static void SetupTempProfile()
        {
            try
            {
                tempProfilePath = Path.Combine(Path.GetTempPath(), $"M3U8Extractor_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempProfilePath);
                Console.WriteLine("🔇 Temporäres Browser-Profil erstellt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Temporäres Profil Fehler: {ex.Message}");
            }
        }

        private static void CleanupTempProfile(string tempPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) && Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                    Console.WriteLine("🧹 Temporäres Profil bereinigt");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cleanup Fehler: {ex.Message}");
            }
        }

        private static void ShowFinalResults()
        {
            Console.WriteLine();
            //Console.WriteLine("=" * 60);
            Console.WriteLine($"🎉 EXTRAKTION ABGESCHLOSSEN");
            //Console.WriteLine("=" * 60);
            Console.WriteLine($"📺 Serie: {currentSeries.Name}");
            Console.WriteLine($"⏱️  Laufzeit: {DateTime.Now - currentSeries.StartedAt:hh\\:mm\\:ss}");
            Console.WriteLine($"📊 Staffeln: {currentSeries.Seasons.Count}");
            Console.WriteLine($"📈 Episoden: {currentSeries.FoundEpisodes}/{currentSeries.TotalEpisodes} erfolgreich");
            Console.WriteLine($"❌ Fehler: {currentSeries.ErrorEpisodes}");
            Console.WriteLine();

            foreach (var season in currentSeries.Seasons.OrderBy(s => s.Number))
            {
                Console.WriteLine($"   🎬 Staffel {season.Number}: {season.FoundEpisodes}/{season.TotalEpisodes} Episoden");
            }

            Console.WriteLine();
            Console.WriteLine("📄 Generierte Dateien:");
            Console.WriteLine($"   📊 {currentSeries.CleanName}_episodes.csv");
            Console.WriteLine($"   💾 {currentSeries.CleanName}_extraction.json");

            foreach (var season in currentSeries.Seasons.Where(s => s.FoundEpisodes > 0))
            {
                Console.WriteLine($"   🎬 {currentSeries.CleanName}_S{season.Number:D2}_yt-dlp.bat");
            }

            Console.WriteLine();
            Console.WriteLine("🎯 VERWENDUNG:");
            Console.WriteLine("   • Führen Sie die .bat Dateien für automatische Downloads aus");
            Console.WriteLine("   • Öffnen Sie die .json Datei für detaillierte Informationen");
            Console.WriteLine("   • Importieren Sie die .csv Datei in Excel/Sheets für Analyse");
            Console.WriteLine();
            Console.WriteLine("Drücken Sie eine Taste zum Beenden...");
            Console.ReadKey();
        }

        private static void LogError(string message)
        {
            errorCount++;
            Console.WriteLine($"❌ Fehler {errorCount}/{maxErrors}: {message}");

            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR {errorCount}: {message}\n";
                File.AppendAllText("errors.log", logEntry);
            }
            catch { }
        }
    }
}