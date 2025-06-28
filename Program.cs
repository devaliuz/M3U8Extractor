using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace M3U8Extractor
{
    class Program
    {
        private static ChromeDriver driver;
        private static HashSet<string> capturedUrls = new HashSet<string>();
        private static string batchFilePath = "yt-dlp_downloads.bat";
        private static int errorCount = 0;
        private static int maxErrors = 3;
        private static string userInputUrl = "";
        private static int currentSeason = 1;
        private static List<string> seasonBatches = new List<string>();
        private static string tempProfilePath = "";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== M3U8 URL Extractor für yt-dlp ===");
            Console.WriteLine();

            // User-Eingabe für die Seite
            Console.Write("Geben Sie die URL der ersten Episode ein: ");
            userInputUrl = Console.ReadLine();

            if (string.IsNullOrEmpty(userInputUrl))
            {
                Console.WriteLine("Keine URL eingegeben. Programm wird beendet.");
                return;
            }

            Console.WriteLine($"Ziel-URL: {userInputUrl}");
            Console.WriteLine("Drücken Sie ENTER zum Starten...");
            Console.ReadLine();

            // Chrome Driver Setup - Silent (Headless) mit einfachem temporären Profil
            var options = new ChromeOptions();
            options.AddArgument("--headless"); // Silent Browser
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            options.AddArgument("--disable-web-security");
            options.AddArgument("--disable-features=VizDisplayCompositor");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");

            // === OPTIONEN UM STÖRENDE MELDUNGEN ZU UNTERDRÜCKEN ===
            options.AddArgument("--disable-logging");
            options.AddArgument("--disable-gpu-sandbox");
            options.AddArgument("--disable-software-rasterizer");
            options.AddArgument("--disable-background-timer-throttling");
            options.AddArgument("--disable-backgrounding-occluded-windows");
            options.AddArgument("--disable-renderer-backgrounding");
            options.AddArgument("--disable-features=TranslateUI");
            options.AddArgument("--disable-ipc-flooding-protection");
            options.AddArgument("--disable-default-apps");
            options.AddArgument("--disable-sync");
            options.AddArgument("--disable-background-networking");
            options.AddArgument("--disable-component-extensions-with-background-pages");
            options.AddArgument("--disable-client-side-phishing-detection");
            options.AddArgument("--disable-hang-monitor");
            options.AddArgument("--disable-prompt-on-repost");
            options.AddArgument("--disable-domain-reliability");
            options.AddArgument("--disable-features=VizDisplayCompositor,VizHitTestSurfaceLayer");
            options.AddArgument("--log-level=3"); // Nur FATAL-Fehler anzeigen
            options.AddArgument("--silent");

            // Service-Optionen deaktivieren
            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            // Einfaches temporäres Verzeichnis (ohne Chrome-Konflikt)
            try
            {
                tempProfilePath = Path.Combine(Path.GetTempPath(), $"M3U8Extractor_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempProfilePath);
                options.AddArgument($"--user-data-dir={tempProfilePath}");
                Console.WriteLine("🔇 Silent Browser mit temporärem Profil gestartet");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Erstellen des temporären Profils: {ex.Message}");
                Console.WriteLine("⚠️  Verwende Standard-Profil");
            }

            driver = new ChromeDriver(service, options);

            try
            {
                // JavaScript für Netzwerk-Monitoring injizieren
                SetupNetworkMonitoring();

                // Initiale Staffel aus URL ermitteln
                driver.Navigate().GoToUrl(userInputUrl);
                await WaitForPageLoad();
                currentSeason = GetSeasonFromCurrentUrl();

                // Batch-Datei mit Staffel-Nummer initialisieren
                batchFilePath = $"yt-dlp_downloads_S{currentSeason}.bat";
                seasonBatches.Add(batchFilePath);
                InitializeYtDlpBatchFile();

                Console.WriteLine($"🎬 Starte mit Staffel {currentSeason}");

                // Episoden-Extraktion starten
                await ExtractEpisodesWithErrorHandling();
            }
            catch (Exception ex)
            {
                LogError($"Kritischer Fehler: {ex.Message}");
                errorCount = maxErrors; // Sofortiger Abbruch bei kritischen Fehlern
            }
            finally
            {
                driver?.Quit();
                FinalizeBatchFile();

                // Temporäres Profil bereinigen
                CleanupTempProfile(tempProfilePath);

                Console.WriteLine();
                Console.WriteLine($"Extrahierte URLs: {capturedUrls.Count}");
                Console.WriteLine($"Aufgetretene Fehler: {errorCount}/{maxErrors}");
                Console.WriteLine();

                // Liste aller generierten Batch-Dateien
                if (seasonBatches.Count > 0)
                {
                    Console.WriteLine("📄 Generierte Batch-Dateien nach Staffeln:");
                    foreach (var batch in seasonBatches)
                    {
                        if (File.Exists(batch))
                        {
                            Console.WriteLine($"   ✅ {batch}");
                        }
                    }
                }

                // Aktuelle Batch-Datei hinzufügen falls noch nicht in der Liste
                if (File.Exists(batchFilePath) && !seasonBatches.Contains(batchFilePath))
                {
                    Console.WriteLine($"   ✅ {batchFilePath}");
                }

                Console.WriteLine();
                Console.WriteLine("📝 DATEINAMEN-FORMAT: [SerienName]S[Staffel]F[Folge].mp4");
                Console.WriteLine("   Beispiel: One_PieceS1F1.mp4, One_PieceS2F1.mp4");
                Console.WriteLine();
                Console.WriteLine("🎯 VERWENDUNG:");
                Console.WriteLine("   Starten Sie jede Batch-Datei einzeln für die gewünschte Staffel");
                Console.WriteLine("   Oder führen Sie alle nacheinander aus");

                if (errorCount >= maxErrors)
                {
                    Console.WriteLine("⚠️  Maximale Fehleranzahl erreicht - Scraping beendet");
                }
                else
                {
                    Console.WriteLine("✅ Scraping erfolgreich abgeschlossen");
                }

                Console.WriteLine();
                Console.WriteLine("Drücken Sie eine Taste zum Beenden...");
                Console.ReadKey();
            }
        }

        private static void SetupNetworkMonitoring()
        {
            // Einfaches JavaScript für grundlegendes Monitoring
            var script = @"
                window.foundM3U8URLs = [];
                
                console.log('*** Basic network monitoring activated ***');
            ";

            ((IJavaScriptExecutor)driver).ExecuteScript(script);
            Console.WriteLine("📡 Basis Netzwerk-Monitoring aktiviert");
        }

        private static void CheckForNewM3U8URLs()
        {
            try
            {
                // Hole gefundene URLs aus dem Browser
                var foundUrls = ((IJavaScriptExecutor)driver).ExecuteScript("return window.foundM3U8URLs || [];") as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                if (foundUrls != null)
                {
                    foreach (var urlObj in foundUrls)
                    {
                        var url = urlObj?.ToString();
                        if (!string.IsNullOrEmpty(url) && !capturedUrls.Contains(url))
                        {
                            capturedUrls.Add(url);
                            Console.WriteLine($"✅ M3U8 URL gefunden: {url}");
                            AddToYtDlpBatch(url);
                        }
                    }
                }

                // Zusätzlich: Suche nach M3U8 URLs im Seitenquelltext
                SearchInPageSource();

                // NEUE METHODE: Direkte Suche nach Performance Entries (Network Requests)
                SearchInPerformanceEntries();

                // Reset der gefundenen URLs im Browser
                ((IJavaScriptExecutor)driver).ExecuteScript("window.foundM3U8URLs = [];");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Überprüfen von M3U8 URLs: {ex.Message}");
            }
        }

        private static void SearchInPerformanceEntries()
        {
            try
            {
                // Nutze Performance API um alle Netzwerk-Requests zu finden
                var script = @"
                    var entries = performance.getEntriesByType('resource');
                    var m3u8Urls = [];
                    entries.forEach(function(entry) {
                        if (entry.name.includes('.m3u8') || entry.name.includes('master.m3u8') || entry.name.includes('playlist.m3u8')) {
                            m3u8Urls.push(entry.name);
                        }
                    });
                    return m3u8Urls;
                ";

                var performanceUrls = ((IJavaScriptExecutor)driver).ExecuteScript(script) as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                if (performanceUrls != null)
                {
                    foreach (var urlObj in performanceUrls)
                    {
                        var url = urlObj?.ToString();
                        if (!string.IsNullOrEmpty(url) && !capturedUrls.Contains(url))
                        {
                            capturedUrls.Add(url);
                            Console.WriteLine($"✅ M3U8 URL über Performance API gefunden: {url}");
                            AddToYtDlpBatch(url);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler bei Performance API Suche: {ex.Message}");
            }
        }

        private static void SearchInPageSource()
        {
            try
            {
                var pageSource = driver.PageSource;
                var m3u8Pattern = @"https?://[^\s""'<>]+\.m3u8[^\s""'<>]*";
                var matches = Regex.Matches(pageSource, m3u8Pattern, RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    var url = match.Value.Trim('"', '\'', '<', '>');
                    if (!capturedUrls.Contains(url))
                    {
                        capturedUrls.Add(url);
                        Console.WriteLine($"✅ M3U8 URL im Quelltext gefunden: {url}");
                        AddToYtDlpBatch(url);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler bei Quelltext-Suche: {ex.Message}");
            }
        }

        private static async Task ExtractEpisodesWithErrorHandling()
        {
            int episodeCounter = 1;
            int consecutiveErrors = 0;
            int episodesProcessedInCurrentSeason = 0;

            while (errorCount < maxErrors)
            {
                try
                {
                    Console.WriteLine($"\n📺 Verarbeite Staffel {currentSeason}, Episode {episodeCounter}...");

                    // Warten bis Seite geladen ist
                    await WaitForPageLoad();

                    // Prüfe aktuelle Staffel aus URL
                    var detectedSeason = GetSeasonFromCurrentUrl();
                    Console.WriteLine($"🔍 Erkannte Staffel aus URL: {detectedSeason}, Aktuelle Staffel: {currentSeason}");

                    // Staffelwechsel erkannt? (Nur wenn es eine höhere Staffel ist UND wir schon Episoden verarbeitet haben)
                    if (detectedSeason != currentSeason && detectedSeason > currentSeason && episodesProcessedInCurrentSeason > 0)
                    {
                        Console.WriteLine($"\n🎬 Staffel {currentSeason} abgeschlossen! ({episodesProcessedInCurrentSeason} Episoden verarbeitet)");
                        FinalizeBatchFile();

                        // Frage User, ob weiter mit nächster Staffel
                        Console.WriteLine($"\n❓ Soll Staffel {detectedSeason} verarbeitet werden? (j/n): ");
                        var userChoice = Console.ReadKey().KeyChar;
                        Console.WriteLine();

                        if (userChoice == 'j' || userChoice == 'J' || userChoice == 'y' || userChoice == 'Y')
                        {
                            currentSeason = detectedSeason;
                            episodeCounter = 1;
                            episodesProcessedInCurrentSeason = 0;

                            // Neue Batch-Datei für neue Staffel
                            batchFilePath = $"yt-dlp_downloads_S{currentSeason}.bat";
                            seasonBatches.Add(batchFilePath);
                            InitializeYtDlpBatchFile();

                            Console.WriteLine($"🚀 Starte Verarbeitung von Staffel {currentSeason}...");
                        }
                        else
                        {
                            Console.WriteLine("🛑 Verarbeitung beendet auf User-Wunsch");
                            break;
                        }
                    }
                    // Falls erkannte Staffel niedriger ist, aktualisiere nur currentSeason (ohne Neustart)
                    else if (detectedSeason != currentSeason)
                    {
                        Console.WriteLine($"🔄 Staffel-Korrektur: {currentSeason} → {detectedSeason}");
                        currentSeason = detectedSeason;
                    }

                    // Stream-Links suchen und anklicken
                    bool streamFound = await TryActivateStreams();

                    if (!streamFound)
                    {
                        LogError($"Keine Stream-Links auf Staffel {currentSeason}, Episode {episodeCounter} gefunden");
                        consecutiveErrors++;
                    }
                    else
                    {
                        consecutiveErrors = 0; // Reset bei Erfolg

                        // Prüfe ob M3U8 URLs gefunden wurden
                        if (capturedUrls.Count > episodesProcessedInCurrentSeason)
                        {
                            episodesProcessedInCurrentSeason++;
                            Console.WriteLine($"✅ Episode {episodeCounter} erfolgreich verarbeitet! (Gesamt in Staffel {currentSeason}: {episodesProcessedInCurrentSeason})");
                        }
                    }

                    // Warte und prüfe auf M3U8 URLs (länger warten)
                    Thread.Sleep(10000); // 10 Sekunden statt 5
                    CheckForNewM3U8URLs();

                    // Zusätzliche Prüfung nach weiteren 5 Sekunden
                    Thread.Sleep(5000);
                    CheckForNewM3U8URLs();

                    // Versuche zur nächsten Episode zu navigieren
                    bool nextEpisodeExists = await NavigateToNextEpisode();

                    if (!nextEpisodeExists)
                    {
                        Console.WriteLine($"🔍 Keine weitere Episode in Staffel {currentSeason} gefunden");
                        Console.WriteLine($"📊 Staffel {currentSeason} Statistik: {episodesProcessedInCurrentSeason} Episoden verarbeitet");

                        // Nur zur nächsten Staffel wenn wir mindestens eine Episode verarbeitet haben
                        if (episodesProcessedInCurrentSeason > 0)
                        {
                            // Prüfe ob es noch weitere Staffeln gibt
                            bool nextSeasonExists = await TryNavigateToNextSeason();

                            if (!nextSeasonExists)
                            {
                                Console.WriteLine("🏁 Keine weiteren Staffeln verfügbar - Scraping beendet");
                                break;
                            }
                            else
                            {
                                episodeCounter = 0; // Reset für neue Staffel (wird unten inkrementiert)
                                episodesProcessedInCurrentSeason = 0; // Reset für neue Staffel
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Keine Episoden in dieser Staffel verarbeitet - beende Scraping");
                            break;
                        }
                    }

                    episodeCounter++;

                    // Kurze Pause zwischen Episoden
                    Thread.Sleep(3000);

                    // Bei zu vielen aufeinanderfolgenden Fehlern abbrechen
                    if (consecutiveErrors >= 2)
                    {
                        LogError("Zu viele aufeinanderfolgende Fehler");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Fehler bei Staffel {currentSeason}, Episode {episodeCounter}: {ex.Message}");
                    consecutiveErrors++;

                    // Bei wiederholten Fehlern längere Pause
                    Thread.Sleep(5000);
                }
            }
        }

        private static async Task<bool> TryActivateStreams()
        {
            bool streamFound = false;
            int maxAttempts = 7;
            int attempt = 0;

            // Verschiedene Selektoren für serienstream.to-ähnliche Seiten
            var streamSelectors = new[]
            {
                ".watchEpisode",                    // Haupt-Stream-Link
                ".hosterSiteVideoButton",          // Stream-Button
                ".generateInlinePlayer a",         // Inline-Player Link
                "a[href*='redirect']",             // Redirect-Links
                "li[data-link-target] a",          // Stream-Hoster Links
                ".episodeLink a"                   // Alternative Episode-Links
            };

            while (attempt < maxAttempts)
            {
                attempt++;
                Console.WriteLine($"🔄 Versuch {attempt}/{maxAttempts} - Suche nach Stream-Links...");

                foreach (var selector in streamSelectors)
                {
                    try
                    {
                        var elements = driver.FindElements(By.CssSelector(selector));
                        Console.WriteLine($"🔍 Gefunden {elements.Count} Elemente für Selektor: {selector}");

                        if (elements.Count > 0)
                        {
                            // Klicke auf die ersten paar Elemente (verschiedene Hoster)
                            int maxClicks = Math.Min(elements.Count, 3);

                            for (int i = 0; i < maxClicks; i++)
                            {
                                try
                                {
                                    var element = elements[i];

                                    // JavaScript-Click für bessere Kompatibilität
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);
                                    Console.WriteLine($"🖱️  Versuch {attempt}: Link {i + 1} angeklickt");

                                    streamFound = true;
                                    Thread.Sleep(3000); // 3 Sekunden warten

                                    // === NEUE METHODE: REDIRECT-URLs DIREKT FOLGEN ===
                                    bool m3u8Found = await FollowRedirectsToVideoPages();

                                    if (m3u8Found && capturedUrls.Count > 0)
                                    {
                                        Console.WriteLine($"✅ M3U8 URL über redirect gefunden! Fahre mit nächster Episode fort.");
                                        return true;
                                    }

                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"⚠️  Fehler beim Klicken auf Element {i + 1}: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Selektor {selector} nicht verfügbar: {ex.Message}");
                    }
                }

                // Warte 3 Sekunden vor dem nächsten Versuch (außer beim letzten Versuch)
                if (attempt < maxAttempts)
                {
                    Console.WriteLine($"⏳ Warte 3 Sekunden vor Versuch {attempt + 1}...");
                    Thread.Sleep(3000);
                }

                // Prüfe nochmal auf M3U8 URLs am Ende jedes Versuchs
                CheckForNewM3U8URLs();
                if (capturedUrls.Count > 0)
                {
                    Console.WriteLine($"✅ M3U8 URL gefunden nach Versuch {attempt}!");
                    return true;
                }
            }

            if (capturedUrls.Count == 0)
            {
                Console.WriteLine($"❌ Keine M3U8 URL nach {maxAttempts} Versuchen gefunden");
                Console.WriteLine($"🛑 KRITISCHER FEHLER: Maximale Versuche erreicht - Prozess wird abgebrochen!");

                // Setze Error-Counter auf Maximum um gesamten Prozess zu beenden
                errorCount = maxErrors;
                return false;
            }

            return streamFound;
        }

        private static async Task<bool> FollowRedirectsToVideoPages()
        {
            Console.WriteLine("🔗 Suche nach redirect-URLs...");
            var originalUrl = driver.Url; // Merke dir die ursprüngliche URL

            try
            {
                // Warte auf iframes mit redirect-URLs
                Thread.Sleep(5000);

                var iframes = driver.FindElements(By.TagName("iframe"));
                Console.WriteLine($"🖼️  {iframes.Count} iframe(s) gefunden");

                foreach (var iframe in iframes)
                {
                    try
                    {
                        var src = iframe.GetAttribute("src");
                        if (!string.IsNullOrEmpty(src))
                        {
                            Console.WriteLine($"🔗 Iframe src: {src}");

                            // Prüfe ob es ein redirect-Link ist
                            if (src.Contains("redirect") || src.Contains("/redirect/"))
                            {
                                Console.WriteLine($"🎯 Redirect-URL gefunden: {src}");

                                // Baue vollständige URL wenn nötig
                                string fullUrl = src;
                                if (src.StartsWith("/"))
                                {
                                    var baseUri = new Uri(driver.Url);
                                    fullUrl = $"{baseUri.Scheme}://{baseUri.Host}{src}";
                                }

                                Console.WriteLine($"🌐 Navigiere zu Video-Seite: {fullUrl}");

                                // Öffne die Video-Seite direkt
                                driver.Navigate().GoToUrl(fullUrl);

                                // Warte bis Video-Seite geladen ist
                                await WaitForPageLoad();
                                Thread.Sleep(5000); // Grundwartezeit für Video-Seite

                                Console.WriteLine($"📺 Video-Seite geladen: {driver.Url}");

                                // Setup Monitoring auf Video-Seite
                                SetupVideoPageNetworkMonitoring();

                                // === NEUE LOGIK: PLAY-BUTTON AUF VIDEO-SEITE DRÜCKEN ===
                                bool m3u8Found = await TryPlayButtonsOnVideoPage();

                                if (m3u8Found && capturedUrls.Count > 0)
                                {
                                    Console.WriteLine($"✅ M3U8 URL auf Video-Seite gefunden!");

                                    // Zurück zur ursprünglichen Seite
                                    driver.Navigate().GoToUrl(originalUrl);
                                    await WaitForPageLoad();

                                    return true;
                                }

                                Console.WriteLine($"❌ Keine M3U8 auf Video-Seite gefunden: {driver.Url}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Fehler beim redirect-Follow: {ex.Message}");
                    }
                }

                // Zurück zur ursprünglichen Seite
                if (driver.Url != originalUrl)
                {
                    driver.Navigate().GoToUrl(originalUrl);
                    await WaitForPageLoad();
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler bei redirect-Verfolgung: {ex.Message}");

                // Sicherheit: Zurück zur ursprünglichen Seite
                try
                {
                    if (driver.Url != originalUrl)
                    {
                        driver.Navigate().GoToUrl(originalUrl);
                        await WaitForPageLoad();
                    }
                }
                catch { }

                return false;
            }
        }

        private static async Task<bool> TryPlayButtonsOnVideoPage()
        {
            Console.WriteLine("🎮 Suche nach Play-Buttons auf Video-Seite...");

            int maxAttempts = 7;

            // Verschiedene Play-Button Selektoren für Video-Player
            var playButtonSelectors = new[]
            {
                ".vjs-big-play-button",          // Video.js Player
                ".jw-display-icon-container",    // JW Player
                ".plyr__controls button[data-plyr='play']", // Plyr Player
                "button[aria-label*='play']",    // Accessibility Play Button
                ".play-button",                  // Generic Play Button
                ".video-play-button",            // Video Play Button
                "[class*='play']",               // Allgemeine Play-Klassen
                "button[title*='play']",         // Play Button mit Title
                ".player-play-button",           // Player-spezifische Buttons
                "video",                         // Direktes Video-Element
                "[onclick*='play']",             // JavaScript Play Handler
                ".btn-play",                     // Bootstrap Play Button
                ".fa-play",                      // FontAwesome Play Icon
                "[data-action='play']"           // Data-Action Play
            };

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Console.WriteLine($"🎮 Video-Seite Versuch {attempt}/{maxAttempts} - Suche Play-Buttons...");

                foreach (var selector in playButtonSelectors)
                {
                    try
                    {
                        var elements = driver.FindElements(By.CssSelector(selector));

                        if (elements.Count > 0)
                        {
                            Console.WriteLine($"🎯 {elements.Count} Play-Element(e) für '{selector}' gefunden");

                            // Versuche mit den ersten paar Elementen
                            int maxClicks = Math.Min(elements.Count, 2);

                            for (int i = 0; i < maxClicks; i++)
                            {
                                try
                                {
                                    var element = elements[i];

                                    // Prüfe ob Element sichtbar/klickbar ist
                                    if (element.Displayed && element.Enabled)
                                    {
                                        Console.WriteLine($"🖱️  Video-Seite Versuch {attempt}: Play-Button {i + 1} klicken");

                                        // JavaScript-Click für bessere Kompatibilität
                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);

                                        // Warte 3 Sekunden nach Klick
                                        Thread.Sleep(3000);

                                        // Prüfe sofort auf M3U8 URLs
                                        await CheckForM3U8OnVideoPage();

                                        if (capturedUrls.Count > 0)
                                        {
                                            Console.WriteLine($"✅ M3U8 URL nach Play-Button Klick gefunden!");
                                            return true;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"⚠️  Fehler beim Play-Button Klick: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Play-Button Selektor '{selector}' nicht verfügbar: {ex.Message}");
                    }
                }

                // Zusätzlich: Versuche auch Klick auf das Video-Element selbst
                try
                {
                    var videoElements = driver.FindElements(By.TagName("video"));
                    if (videoElements.Count > 0)
                    {
                        Console.WriteLine($"📹 {videoElements.Count} Video-Element(e) gefunden - versuche direkten Klick");

                        foreach (var video in videoElements.Take(2))
                        {
                            try
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].play();", video);
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", video);
                                Console.WriteLine($"🎬 Video-Element direkt aktiviert");

                                Thread.Sleep(3000);
                                await CheckForM3U8OnVideoPage();

                                if (capturedUrls.Count > 0)
                                {
                                    Console.WriteLine($"✅ M3U8 URL nach Video-Element Aktivierung gefunden!");
                                    return true;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"⚠️  Fehler bei Video-Element Aktivierung: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Fehler bei Video-Element Suche: {ex.Message}");
                }

                // Warte zwischen den Versuchen (außer beim letzten)
                if (attempt < maxAttempts)
                {
                    Console.WriteLine($"⏳ Video-Seite: Warte 3 Sekunden vor Versuch {attempt + 1}...");
                    Thread.Sleep(3000);

                    // Prüfe nochmal auf M3U8 URLs (falls sie durch Werbung verzögert geladen werden)
                    await CheckForM3U8OnVideoPage();
                    if (capturedUrls.Count > 0)
                    {
                        Console.WriteLine($"✅ M3U8 URL nach Wartezeit gefunden!");
                        return true;
                    }
                }
            }

            Console.WriteLine($"❌ Keine M3U8 URLs nach {maxAttempts} Play-Versuchen auf Video-Seite gefunden");
            return false;
        }

        private static void SetupVideoPageNetworkMonitoring()
        {
            // Spezielles Monitoring für Video-Seiten
            var script = @"
                window.foundM3U8URLs = window.foundM3U8URLs || [];
                
                console.log('Setting up video page monitoring...');
                
                // Override fetch
                const originalFetch = window.fetch;
                if (originalFetch) {
                    window.fetch = function(...args) {
                        const url = args[0];
                        if (typeof url === 'string') {
                            console.log('VIDEO PAGE FETCH:', url);
                            if (url.includes('.m3u8') || url.includes('master.m3u8') || url.includes('playlist.m3u8') || url.includes('index.m3u8') || url.includes('.ts')) {
                                window.foundM3U8URLs.push(url);
                                console.log('*** M3U8/TS URL FOUND on video page:', url);
                            }
                        }
                        return originalFetch.apply(this, args);
                    };
                }
                
                // Override XMLHttpRequest
                const originalOpen = XMLHttpRequest.prototype.open;
                XMLHttpRequest.prototype.open = function(method, url, ...args) {
                    if (typeof url === 'string') {
                        console.log('VIDEO PAGE XHR:', url);
                        if (url.includes('.m3u8') || url.includes('master.m3u8') || url.includes('playlist.m3u8') || url.includes('index.m3u8') || url.includes('.ts')) {
                            window.foundM3U8URLs.push(url);
                            console.log('*** M3U8/TS URL FOUND on video page:', url);
                        }
                    }
                    return originalOpen.apply(this, [method, url, ...args]);
                };
                
                console.log('*** Video page network monitoring activated ***');
            ";

            ((IJavaScriptExecutor)driver).ExecuteScript(script);
            Console.WriteLine("📡 Video-Seiten Netzwerk-Monitoring aktiviert");
        }

        private static async Task CheckForM3U8OnVideoPage()
        {
            try
            {
                var episodeM3U8Found = false; // Flag um nur eine M3U8 pro Episode zu nehmen

                // Prüfe auf M3U8 URLs im JavaScript
                var foundUrls = ((IJavaScriptExecutor)driver).ExecuteScript("return window.foundM3U8URLs || [];") as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                if (foundUrls != null)
                {
                    foreach (var urlObj in foundUrls)
                    {
                        var url = urlObj?.ToString();
                        if (!string.IsNullOrEmpty(url) && !capturedUrls.Contains(url))
                        {
                            // Filtere nur echte M3U8 URLs (nicht .ts segments oder Tracking)
                            if (IsValidM3U8Url(url))
                            {
                                // Priorisiere master.m3u8 über andere
                                if (url.Contains("master.m3u8") || !episodeM3U8Found)
                                {
                                    capturedUrls.Add(url);
                                    Console.WriteLine($"✅ M3U8 URL auf Video-Seite gefunden: {url}");
                                    AddToYtDlpBatch(url);
                                    episodeM3U8Found = true;

                                    // Bei master.m3u8 können wir aufhören
                                    if (url.Contains("master.m3u8"))
                                    {
                                        Console.WriteLine("🎯 Master.m3u8 gefunden - beste Qualität gewählt");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // Performance API als Backup (nur wenn noch nichts gefunden)
                if (!episodeM3U8Found)
                {
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

                    var performanceUrls = ((IJavaScriptExecutor)driver).ExecuteScript(script) as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                    if (performanceUrls != null)
                    {
                        foreach (var urlObj in performanceUrls)
                        {
                            var url = urlObj?.ToString();
                            if (!string.IsNullOrEmpty(url) && !capturedUrls.Contains(url) && IsValidM3U8Url(url))
                            {
                                capturedUrls.Add(url);
                                Console.WriteLine($"✅ M3U8 URL über Performance API auf Video-Seite gefunden: {url}");
                                AddToYtDlpBatch(url);
                                break; // Nur eine URL pro Episode
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim M3U8-Check auf Video-Seite: {ex.Message}");
            }
        }

        private static bool IsValidM3U8Url(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            // Muss .m3u8 enthalten
            if (!url.Contains(".m3u8")) return false;

            // Filtere Tracking/Analytics URLs aus
            if (url.Contains("jwplayer") && url.Contains(".gif")) return false;
            if (url.Contains("analytics")) return false;
            if (url.Contains("tracking")) return false;
            if (url.Contains("stats")) return false;

            // Muss eine gültige HTTP(S) URL sein
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) return false;

            Console.WriteLine($"🔍 Validiere M3U8 URL: {url}");
            return true;
        }

        private static async Task<bool> WaitForPageLoad()
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                wait.Until(driver => ((IJavaScriptExecutor)driver)
                    .ExecuteScript("return document.readyState").Equals("complete"));

                Thread.Sleep(3000); // Zusätzliche Wartezeit für dynamischen Inhalt
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Timeout beim Laden der Seite: {ex.Message}");
                return false;
            }
        }

        private static async Task HandleNewWindows()
        {
            try
            {
                if (driver.WindowHandles.Count > 1)
                {
                    // Zu neuem Fenster wechseln
                    driver.SwitchTo().Window(driver.WindowHandles[^1]);
                    var currentUrl = driver.Url;
                    Console.WriteLine($"📱 Neues Fenster: {currentUrl}");

                    // Längere Wartezeit für Stream-Loading
                    Thread.Sleep(5000);

                    // Setup Monitoring im neuen Fenster
                    SetupNetworkMonitoring();

                    // Warte auf Stream-Loading (bis zu 15 Sekunden)
                    for (int i = 0; i < 5; i++)
                    {
                        Thread.Sleep(3000);
                        CheckForNewM3U8URLs();

                        // Prüfe auch direkt nach iframes im neuen Fenster
                        try
                        {
                            var iframes = driver.FindElements(By.TagName("iframe"));
                            foreach (var iframe in iframes)
                            {
                                var src = iframe.GetAttribute("src");
                                if (!string.IsNullOrEmpty(src))
                                {
                                    Console.WriteLine($"🖼️  Iframe gefunden: {src}");

                                    // Prüfe iframe src auf stream-Hinweise
                                    if (src.Contains("player") || src.Contains("embed") || src.Contains("stream"))
                                    {
                                        Console.WriteLine($"🎬 Stream-iframe erkannt: {src}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️  Fehler beim iframe-Check: {ex.Message}");
                        }

                        // Früher Ausstieg wenn M3U8 gefunden
                        if (capturedUrls.Count > 0)
                        {
                            Console.WriteLine($"✅ M3U8 URL in neuem Fenster gefunden!");
                            break;
                        }
                    }

                    // Schließe das neue Fenster und kehre zum Hauptfenster zurück
                    driver.Close();
                    driver.SwitchTo().Window(driver.WindowHandles[0]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Behandeln neuer Fenster: {ex.Message}");

                // Sicherheit: Zurück zum ersten Fenster
                try
                {
                    driver.SwitchTo().Window(driver.WindowHandles[0]);
                }
                catch { }
            }
        }

        private static async Task<bool> NavigateToNextEpisode()
        {
            try
            {
                Console.WriteLine("🔍 Suche nach nächster Episode...");
                var originalUrl = driver.Url; // Merke dir die ursprüngliche URL

                // KORRIGIERT: Spezifischer Selektor NUR für EPISODEN (nicht Staffeln)
                // Episoden haben data-episode-id Attribut
                var episodeLinks = driver.FindElements(By.CssSelector("a[data-episode-id]"));
                Console.WriteLine($"📋 {episodeLinks.Count} Episode-Links gefunden");

                if (episodeLinks.Count > 0)
                {
                    // Finde den aktiven Episode-Link
                    var activeEpisodeLink = episodeLinks.FirstOrDefault(link =>
                        link.GetAttribute("class")?.Contains("active") == true);

                    if (activeEpisodeLink != null)
                    {
                        var activeIndex = episodeLinks.ToList().IndexOf(activeEpisodeLink);
                        var currentEpisodeId = activeEpisodeLink.GetAttribute("data-episode-id");
                        var currentEpisodeTitle = activeEpisodeLink.GetAttribute("title");

                        Console.WriteLine($"📍 Aktuelle Episode: {currentEpisodeTitle} (ID: {currentEpisodeId}, Index: {activeIndex})");

                        // Klicke auf den nächsten EPISODE-Link
                        if (activeIndex + 1 < episodeLinks.Count)
                        {
                            var nextEpisodeLink = episodeLinks[activeIndex + 1];
                            var nextEpisodeId = nextEpisodeLink.GetAttribute("data-episode-id");
                            var nextEpisodeTitle = nextEpisodeLink.GetAttribute("title");
                            var expectedUrl = nextEpisodeLink.GetAttribute("href");

                            Console.WriteLine($"➡️  Navigiere zu nächster Episode: {nextEpisodeTitle} (ID: {nextEpisodeId})");
                            Console.WriteLine($"🎯 Erwartete URL: {expectedUrl}");

                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", nextEpisodeLink);

                            // Warte bis neue Episode geladen ist
                            await WaitForPageLoad();
                            Thread.Sleep(3000);

                            // KORRIGIERT: Überprüfe ob Navigation erfolgreich war
                            var newUrl = driver.Url;
                            Console.WriteLine($"🔍 Neue URL nach Navigation: {newUrl}");

                            if (newUrl != originalUrl && (newUrl.Contains(expectedUrl) || expectedUrl.Contains(newUrl.Split('?')[0])))
                            {
                                Console.WriteLine($"✅ Link-Navigation erfolgreich");
                                return true;
                            }
                            else
                            {
                                Console.WriteLine($"⚠️  Link-Navigation fehlgeschlagen (URL nicht geändert oder falsch)");
                                Console.WriteLine($"   Original: {originalUrl}");
                                Console.WriteLine($"   Erwartet: {expectedUrl}");
                                Console.WriteLine($"   Aktuell:  {newUrl}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"🏁 Keine weitere Episode nach {currentEpisodeTitle} in dieser Staffel gefunden");
                            return false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("⚠️  Kein aktiver Episode-Link gefunden - versuche URL-Methode");
                    }
                }

                // Methode 2: URL-basierte Navigation (Fallback)
                Console.WriteLine("🔄 Versuche URL-basierte Navigation...");
                var currentUrl = driver.Url;
                var nextUrl = GenerateNextEpisodeUrl(currentUrl);

                if (nextUrl != null && nextUrl != currentUrl)
                {
                    Console.WriteLine($"🌐 Navigiere zu: {nextUrl}");
                    driver.Navigate().GoToUrl(nextUrl);
                    await WaitForPageLoad();

                    // Prüfe ob Seite existiert (kein 404)
                    var pageTitle = driver.Title.ToLower();
                    if (!pageTitle.Contains("404") && !pageTitle.Contains("not found") && !pageTitle.Contains("error"))
                    {
                        Console.WriteLine("✅ URL-basierte Navigation erfolgreich");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("❌ Nächste Episode existiert nicht (404/Error) - Ende der Staffel erreicht");
                        return false;
                    }
                }

                Console.WriteLine("❌ Alle Navigationsmethoden fehlgeschlagen - Ende der Staffel");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Fehler beim Navigieren zur nächsten Episode: {ex.Message}");
                return false;
            }
        }

        private static int GetSeasonFromCurrentUrl()
        {
            try
            {
                var currentUrl = driver.Url;
                var uri = new Uri(currentUrl);
                var segments = uri.Segments;

                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].StartsWith("staffel-"))
                    {
                        var seasonStr = segments[i].Replace("staffel-", "").TrimEnd('/');
                        if (int.TryParse(seasonStr, out int season))
                        {
                            return season;
                        }
                    }
                }

                return 1; // Fallback
            }
            catch
            {
                return 1;
            }
        }

        private static async Task<bool> TryNavigateToNextSeason()
        {
            try
            {
                Console.WriteLine($"🔍 Suche nach Staffel {currentSeason + 1}...");

                // KORRIGIERT: Spezifische Suche nach STAFFEL-Links (nicht Episode-Links)
                // Staffel-Links haben title="Staffel X" und KEINE data-episode-id
                var seasonLinks = driver.FindElements(By.CssSelector(".hosterSiteDirectNav ul li a[title*='Staffel']:not([data-episode-id])"));
                Console.WriteLine($"📺 {seasonLinks.Count} Staffel-Links gefunden");

                // Suche nach Link zur nächsten Staffel
                var nextSeasonNumber = currentSeason + 1;
                var nextSeasonLink = seasonLinks.FirstOrDefault(link =>
                    link.GetAttribute("title")?.Contains($"Staffel {nextSeasonNumber}") == true ||
                    (link.Text.Trim() == nextSeasonNumber.ToString() && link.GetAttribute("title")?.Contains("Staffel") == true));

                if (nextSeasonLink != null)
                {
                    var linkTitle = nextSeasonLink.GetAttribute("title");
                    var linkText = nextSeasonLink.Text.Trim();
                    Console.WriteLine($"🎬 Staffel-Link gefunden: '{linkTitle}' (Text: '{linkText}')");

                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", nextSeasonLink);
                    Console.WriteLine($"➡️  Navigiert zu Staffel {nextSeasonNumber}");

                    // Warte bis Seite geladen ist
                    await WaitForPageLoad();

                    // Navigiere zur ersten Episode der neuen Staffel
                    var firstEpisodeUrl = GenerateEpisodeUrl(nextSeasonNumber, 1);
                    if (firstEpisodeUrl != null)
                    {
                        Console.WriteLine($"🌐 Navigiere zur ersten Episode: {firstEpisodeUrl}");
                        driver.Navigate().GoToUrl(firstEpisodeUrl);
                        await WaitForPageLoad();

                        // Prüfe ob die Episode-Seite erfolgreich geladen wurde
                        var pageTitle = driver.Title.ToLower();
                        if (!pageTitle.Contains("404") && !pageTitle.Contains("not found"))
                        {
                            Console.WriteLine($"✅ Erfolgreich zur ersten Episode von Staffel {nextSeasonNumber} navigiert");
                            return true;
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Kein Link zu Staffel {nextSeasonNumber} gefunden");
                }

                // Alternative: URL-basierte Navigation
                Console.WriteLine("🔄 Versuche URL-basierte Staffel-Navigation...");
                var currentUrl = driver.Url;
                var nextSeasonUrl = currentUrl.Replace($"staffel-{currentSeason}", $"staffel-{nextSeasonNumber}")
                                              .Replace($"episode-", "episode-1"); // Erste Episode der neuen Staffel

                if (nextSeasonUrl != currentUrl)
                {
                    Console.WriteLine($"🌐 Teste URL: {nextSeasonUrl}");
                    driver.Navigate().GoToUrl(nextSeasonUrl);
                    await WaitForPageLoad();

                    // Prüfe ob Seite existiert (kein 404)
                    var pageTitle = driver.Title.ToLower();
                    if (!pageTitle.Contains("404") && !pageTitle.Contains("not found"))
                    {
                        Console.WriteLine($"✅ URL-basiert zu Staffel {nextSeasonNumber} navigiert");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Staffel {nextSeasonNumber} existiert nicht (404)");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Navigieren zur nächsten Staffel: {ex.Message}");
                return false;
            }
        }

        private static string GenerateEpisodeUrl(int season, int episode)
        {
            try
            {
                var currentUrl = driver.Url;
                var uri = new Uri(currentUrl);
                var segments = uri.Segments.ToList();

                // Finde und ersetze Staffel- und Episode-Segmente
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i].StartsWith("staffel-"))
                    {
                        segments[i] = $"staffel-{season}/";
                    }
                    else if (segments[i].StartsWith("episode-"))
                    {
                        segments[i] = $"episode-{episode}";
                    }
                }

                var newUrl = uri.Scheme + "://" + uri.Host + string.Join("", segments);
                return newUrl;
            }
            catch
            {
                return null;
            }
        }

        private static string GenerateNextEpisodeUrl(string currentUrl)
        {
            try
            {
                Console.WriteLine($"🔧 Generiere nächste Episode-URL von: {currentUrl}");

                // Pattern für serienstream.to: .../episode-X
                var uri = new Uri(currentUrl);
                var segments = uri.Segments;

                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].StartsWith("episode-"))
                    {
                        var episodeNumberStr = segments[i].Replace("episode-", "").TrimEnd('/');
                        Console.WriteLine($"🔍 Gefundene Episode-Nummer in URL: '{episodeNumberStr}'");

                        if (int.TryParse(episodeNumberStr, out int currentEpisodeNumber))
                        {
                            var nextEpisodeNumber = currentEpisodeNumber + 1;
                            var nextUrl = currentUrl.Replace($"episode-{currentEpisodeNumber}", $"episode-{nextEpisodeNumber}");

                            Console.WriteLine($"📈 URL-Generation: episode-{currentEpisodeNumber} → episode-{nextEpisodeNumber}");
                            Console.WriteLine($"🎯 Generierte nächste URL: {nextUrl}");

                            return nextUrl;
                        }
                        else
                        {
                            Console.WriteLine($"⚠️  Konnte Episode-Nummer nicht parsen: '{episodeNumberStr}'");
                        }
                    }
                }

                Console.WriteLine($"❌ Keine Episode-Nummer in URL gefunden");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Generieren der nächsten Episode-URL: {ex.Message}");
                return null;
            }
        }

        private static void InitializeYtDlpBatchFile()
        {
            var header = @"@echo off
setlocal enabledelayedexpansion
REM ===== yt-dlp Parallel Download System =====
REM Staffel: " + currentSeason + @"
REM Generiert am: " + DateTime.Now + @"
REM Quelle: " + userInputUrl + @"
REM Maximal 2 parallele Downloads

if not exist ""Downloads"" mkdir Downloads
cd Downloads

REM Semaphor-System für max 2 parallele Downloads
set /a running_downloads=0
set /a max_parallel=2

echo.
echo 🚀 Starte Download-System für Staffel " + currentSeason + @" (max %max_parallel% parallel)
echo ================================================
echo.

REM Download-Funktion definieren
goto :start_downloads

:download_episode
set ""url=%~1""
set ""temp_name=%~2""
set ""final_name=%~3""

REM Warte bis Slot frei ist
:wait_for_slot
if !running_downloads! geq %max_parallel% (
    timeout /t 5 /nobreak >nul
    goto :wait_for_slot
)

REM Slot reservieren
set /a running_downloads+=1
echo [!time!] 📥 Start: %final_name% (Slot !running_downloads!/%max_parallel%)

REM Download in Hintergrund starten
start /b """" cmd /c ""call :do_download ""%url%"" ""%temp_name%"" ""%final_name%""&&set /a running_downloads-=1""

goto :eof

:do_download
set ""url=%~1""
set ""temp_name=%~2""
set ""final_name=%~3""

REM Eigentlicher yt-dlp Download
echo [%time%] 🔄 Downloading: %final_name%
yt-dlp ""%url%"" --output ""%temp_name%%.%%(ext)s"" --merge-output-format mp4 --embed-metadata --no-playlist

REM Prüfe ob Download erfolgreich
if %errorlevel% equ 0 (
    REM Datei umbenennen
    for %%f in (""%temp_name%.*"") do (
        set ""ext=%%~xf""
        ren ""%%f"" ""%final_name%!ext!""
        echo [%time%] ✅ Completed: %final_name%!ext!
    )
) else (
    echo [%time%] ❌ Failed: %final_name%
)

REM Slot freigeben
set /a running_downloads-=1
goto :eof

:start_downloads
REM Hier kommen die Download-Aufrufe für Staffel " + currentSeason + @":

";

            File.WriteAllText(batchFilePath, header);
            Console.WriteLine($"📄 Batch-Datei für Staffel {currentSeason} initialisiert: {batchFilePath}");
        }

        private static void AddToYtDlpBatch(string m3u8Url)
        {
            try
            {
                // Extrahiere Episode-Info aus der aktuellen URL
                var currentUrl = driver.Url;
                var (tempName, finalName) = ExtractEpisodeInfo(currentUrl);

                // Batch-Datei: yt-dlp Command mit temporärem Namen und anschließender Umbenennung
                var command = $"call :download_episode \"{m3u8Url}\" \"{tempName}\" \"{finalName}\"\n";
                File.AppendAllText(batchFilePath, command);

                Console.WriteLine($"📝 Download-Command hinzugefügt: {finalName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Hinzufügen zur Batch-Datei: {ex.Message}");
            }
        }

        private static (string tempName, string finalName) ExtractEpisodeInfo(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments;

                var serie = "Unknown_Series";
                var staffel = "1";
                var episode = "1";

                // Extrahiere aus serienstream.to URL-Struktur
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i] == "stream/" && i + 1 < segments.Length)
                    {
                        serie = segments[i + 1].TrimEnd('/').Replace("-", "_");
                        // Capitalize first letter of each word
                        serie = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(serie.Replace("_", " ")).Replace(" ", "_");
                        Console.WriteLine($"📺 Serie aus URL extrahiert: {serie}");
                    }
                    else if (segments[i].StartsWith("staffel-"))
                    {
                        staffel = segments[i].Replace("staffel-", "").TrimEnd('/');
                    }
                    else if (segments[i].StartsWith("episode-"))
                    {
                        episode = segments[i].Replace("episode-", "").TrimEnd('/');
                    }
                }

                // Fallback: Versuche aus Page Title zu extrahieren (aber nur als Fallback!)
                if (serie == "Unknown_Series")
                {
                    try
                    {
                        var pageTitle = driver.Title;
                        Console.WriteLine($"🔍 Versuche Serie aus Titel zu extrahieren: {pageTitle}");

                        // Extrahiere Serienname aus Titel wie "Episode 1 Staffel 1 von One Piece"
                        if (pageTitle.Contains(" von "))
                        {
                            var titleParts = pageTitle.Split(new[] { " von " }, StringSplitOptions.RemoveEmptyEntries);
                            if (titleParts.Length > 1)
                            {
                                var seriesName = titleParts[titleParts.Length - 1].Split('|')[0].Trim();
                                serie = seriesName.Replace(" ", "_").Replace("-", "_");
                                Console.WriteLine($"📺 Serie aus Titel extrahiert: {serie}");
                            }
                        }
                        // Alternative: Suche nach bekannten Anime-/Serien-Namen im Titel
                        else
                        {
                            // Liste bekannter Serien, die im Titel vorkommen könnten
                            var knownSeries = new[] { "One Piece", "Naruto", "Dragon Ball", "Attack on Titan", "Demon Slayer" };

                            foreach (var knownSerie in knownSeries)
                            {
                                if (pageTitle.ToLower().Contains(knownSerie.ToLower()))
                                {
                                    serie = knownSerie.Replace(" ", "_");
                                    Console.WriteLine($"📺 Bekannte Serie im Titel gefunden: {serie}");
                                    break;
                                }
                            }

                            // Wenn immer noch nicht gefunden, versuche ersten Teil vor bestimmten Keywords
                            if (serie == "Unknown_Series")
                            {
                                var keywords = new[] { ".mp4", " ansehen", " - VOE", " |" };
                                var titleStart = pageTitle;

                                foreach (var keyword in keywords)
                                {
                                    if (titleStart.Contains(keyword))
                                    {
                                        titleStart = titleStart.Split(new[] { keyword }, StringSplitOptions.None)[0].Trim();
                                    }
                                }

                                // Nehme die ersten 2-3 Wörter als Seriename
                                var words = titleStart.Split(new[] { '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (words.Length >= 2)
                                {
                                    serie = string.Join("_", words.Take(2));
                                    Console.WriteLine($"📺 Serie aus Titel-Anfang extrahiert: {serie}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Fehler bei Titel-Extraktion: {ex.Message}");
                    }
                }

                // Bereinige Seriennamen
                serie = serie.Replace(".", "_").Replace(",", "_").Replace(":", "_");

                // Temporärer Name für yt-dlp (einfacher)
                var tempName = $"temp_S{staffel}E{episode}";

                // Finaler Name im gewünschten Format
                var finalName = $"{serie}S{staffel}F{episode}";

                Console.WriteLine($"📝 Episode-Info: {finalName}");
                return (tempName, finalName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler bei Episode-Info-Extraktion: {ex.Message}");
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                return ($"temp_{timestamp}", $"Video_{timestamp}");
            }
        }

        private static void FinalizeBatchFile()
        {
            var footer = @"
REM Warte bis alle Downloads abgeschlossen sind
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
echo    - Insgesamt verarbeitet: " + capturedUrls.Count + @" URLs
echo    - Zielordner: %cd%
echo    - Format: [SerienName]S[Staffel]F[Folge].mp4
echo.

REM Zeige heruntergeladene Dateien
echo 📁 Heruntergeladene Dateien:
dir /b *.mp4 2>nul || echo    (Keine .mp4 Dateien gefunden)

echo.
echo ✨ Fertig! Drücken Sie eine Taste zum Beenden...
pause >nul
";

            File.AppendAllText(batchFilePath, footer);
            Console.WriteLine($"📄 Batch-Datei finalisiert mit {capturedUrls.Count} Downloads");
        }

        private static void CleanupTempProfile(string tempProfilePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(tempProfilePath) && Directory.Exists(tempProfilePath))
                {
                    Directory.Delete(tempProfilePath, true);
                    Console.WriteLine("🧹 Temporäres Profil bereinigt");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Fehler beim Bereinigen des temporären Profils: {ex.Message}");
            }
        }

        private static void LogError(string message)
        {
            errorCount++;
            Console.WriteLine($"❌ Fehler {errorCount}/{maxErrors}: {message}");

            // Optional: In Log-Datei schreiben
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR {errorCount}: {message}\n";
            File.AppendAllText("errors.log", logEntry);
        }
    }
}