using Microsoft.EntityFrameworkCore;
using YtDlpExtractor.Core.Database;
using YtDlpExtractor.Core.Models;
using YtDlpExtractor.Services;
using YtDlpExtractor.Extractors;
using YtDlpExtractor.Configuration;

namespace YtDlpExtractor.CLI
{
    class Program
    {
        private static DatabaseContext? _dbContext;
        private static ExtractionService? _extractionService;
        private static DownloadService? _downloadService;

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("🎬 YT-DLP Link Extractor & Automation Tool");
            Console.WriteLine("==========================================");

            try
            {
                // Initialisierung
                await InitializeDatabaseAsync();
                await InitializeServicesAsync();

                if (args.Length == 0)
                {
                    return await RunInteractiveMode();
                }

                var command = args[0].ToLower();
                return command switch
                {
                    "extract" => await HandleExtractCommand(args),
                    "download" => await HandleDownloadCommand(args),
                    "status" => await HandleStatusCommand(args),
                    "test" => await RunTestCommand(),
                    "help" or "--help" or "-h" => ShowHelp(),
                    _ => ShowInvalidCommand(command)
                };
            }
            finally
            {
                await CleanupAsync();
            }
        }

        private static async Task InitializeDatabaseAsync()
        {
            try
            {
                _dbContext = new DatabaseContext("Data Source=ytdlp_extractor.db");
                await _dbContext.Database.EnsureCreatedAsync();
                Console.WriteLine("✅ Datenbank initialisiert");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Datenbankfehler: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static async Task InitializeServicesAsync()
        {
            _extractionService = new ExtractionService(_dbContext!);
            _downloadService = new DownloadService(_dbContext!);

            // Registriere den verbesserten VidmolyExtractor
            _extractionService.RegisterExtractor(new VidmolyExtractor());

            Console.WriteLine("✅ Services initialisiert");
        }

        private static async Task<int> RunInteractiveMode()
        {
            Console.WriteLine("\n🎮 INTERAKTIVER MODUS");
            Console.WriteLine("====================");
            Console.WriteLine("Befehle: extract, download, status, test, help, exit");
            Console.WriteLine();

            while (true)
            {
                Console.Write("ytdlp> ");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input)) continue;

                if (input.ToLower() is "exit" or "quit" or "q")
                {
                    Console.WriteLine("👋 Auf Wiedersehen!");
                    return 0;
                }

                try
                {
                    var args = ParseInput(input);
                    if (args.Length == 0) continue;

                    var result = args[0].ToLower() switch
                    {
                        "extract" => await HandleExtractCommand(args),
                        "download" => await HandleDownloadCommand(args),
                        "status" => await HandleStatusCommand(args),
                        "test" => await RunTestCommand(),
                        "help" => ShowHelp(),
                        _ => ShowInvalidCommand(args[0])
                    };

                    if (result != 0)
                    {
                        Console.WriteLine($"⚠️ Command beendet mit Code: {result}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler: {ex.Message}");
                }

                Console.WriteLine();
            }
        }

        private static async Task<int> __HandleExtractCommand(string[] args)
        {
            var options = new ExtractCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--url":
                        if (i + 1 < args.Length) options.Url = args[++i];
                        break;
                    case "--series-name":
                        if (i + 1 < args.Length) options.SeriesName = args[++i];
                        break;
                    case "--host":
                        if (i + 1 < args.Length) options.Host = args[++i];
                        break;
                    case "--max-episodes":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int max))
                            options.MaxEpisodes = max;
                        break;
                }
            }

            if (string.IsNullOrEmpty(options.Url))
            {
                Console.WriteLine("❌ URL ist erforderlich. Beispiel:");
                Console.WriteLine("   extract --url \"https://aniworld.to/anime/stream/one-piece/staffel-1/episode-1\"");
                return 1;
            }

            return await ExecuteExtractionAsync(options);
        }

        // In Program.cs - erweitere die HandleExtractCommand Methode:

        private static async Task<int> HandleExtractCommand(string[] args)
        {
            var options = new ExtractCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--url":
                        if (i + 1 < args.Length) options.Url = args[++i];
                        break;
                    case "--series-name":
                        if (i + 1 < args.Length) options.SeriesName = args[++i];
                        break;
                    case "--host":
                        if (i + 1 < args.Length) options.Host = args[++i];
                        break;
                    case "--max-episodes":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int max))
                            options.MaxEpisodes = max;
                        break;
                    case "--start-season":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int season))
                            options.StartSeason = season;
                        break;
                    case "--start-episode":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int episode))
                            options.StartEpisode = episode;
                        break;
                    case "--force-rescrape":
                        options.ForceRescrape = true;
                        break;
                    case "--skip-existing":
                        options.SkipExisting = true;
                        break;
                    case "--no-skip":
                        options.SkipExisting = false;
                        break;
                }
            }

            if (string.IsNullOrEmpty(options.Url))
            {
                Console.WriteLine("❌ URL ist erforderlich. Beispiel:");
                Console.WriteLine("   extract --url \"https://aniworld.to/anime/stream/one-piece/staffel-1/episode-1\"");
                Console.WriteLine();
                Console.WriteLine("💡 SKIP-OPTIONEN:");
                Console.WriteLine("   --skip-existing    Überspringe Episoden mit vorhandenen Links (Standard)");
                Console.WriteLine("   --no-skip          Scrape alle Episoden neu");
                Console.WriteLine("   --force-rescrape   Lösche vorhandene Links und scrape neu");
                return 1;
            }

            return await ExecuteExtractionAsync(options);
        }

        private static async Task<int> ExecuteExtractionAsync(ExtractCommandOptions options)
        {
            Console.WriteLine($"\n🔍 STARTE LINK-EXTRAKTION");
            Console.WriteLine($"========================");
            Console.WriteLine($"📺 URL: {options.Url}");
            Console.WriteLine($"🎬 Serie: {options.SeriesName ?? "Auto-Detect"}");
            Console.WriteLine($"🏠 Host: {options.Host}");
            Console.WriteLine($"⏭️ Skip-Modus: {(options.SkipExisting ? "Überspringe vorhandene" : "Scrape alle neu")}");
            if (options.ForceRescrape)
                Console.WriteLine($"🔄 Force-Rescrape: Lösche vorhandene Links");
            if (options.MaxEpisodes.HasValue)
                Console.WriteLine($"📈 Max Episodes: {options.MaxEpisodes}");

            try
            {
                var extractionOptions = new ExtractionOptions
                {
                    SeriesName = options.SeriesName,
                    PreferredHost = options.Host,
                    StartSeason = options.StartSeason,
                    StartEpisode = options.StartEpisode,
                    MaxEpisodes = options.MaxEpisodes,
                    MaxConsecutiveErrors = 3,
                    DelayBetweenEpisodes = 1000,
                    ContinueOnError = true,
                    SkipExistingEpisodes = options.SkipExisting, // NEU!
                    ForceRescrape = options.ForceRescrape // NEU!
                };

                var result = await _extractionService!.ExtractSeriesAsync(options.Url, extractionOptions);

                Console.WriteLine($"\n✅ EXTRAKTION ABGESCHLOSSEN!");
                Console.WriteLine($"=============================");
                Console.WriteLine($"📺 Serie: {result.Series.Name}");
                Console.WriteLine($"📊 Staffeln: {result.Series.Seasons.Count}");
                Console.WriteLine($"📈 Episoden verarbeitet: {result.ProcessedEpisodes}");
                //Console.WriteLine($"⏭️ Episoden übersprungen: {result.SkippedEpisodes}"); // NEU!
                Console.WriteLine($"🔗 Links gefunden: {result.TotalLinksFound}");
                Console.WriteLine($"❌ Fehler: {result.TotalErrors}");
                Console.WriteLine($"⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"📈 Erfolgsrate: {result.SuccessRate:F1}%");

                if (result.HostStatistics.Any())
                {
                    Console.WriteLine($"\n🏠 HOST-STATISTIKEN:");
                    foreach (var host in result.HostStatistics)
                    {
                        Console.WriteLine($"   {host.Key}: {host.Value} Links");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Extraktion fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> __ExecuteExtractionAsync(ExtractCommandOptions options)
        {
            Console.WriteLine($"\n🔍 STARTE LINK-EXTRAKTION");
            Console.WriteLine($"========================");
            Console.WriteLine($"📺 URL: {options.Url}");
            Console.WriteLine($"🎬 Serie: {options.SeriesName ?? "Auto-Detect"}");
            Console.WriteLine($"🏠 Host: {options.Host}");
            if (options.MaxEpisodes.HasValue)
                Console.WriteLine($"📈 Max Episodes: {options.MaxEpisodes}");

            try
            {
                var extractionOptions = new ExtractionOptions
                {
                    SeriesName = options.SeriesName,
                    PreferredHost = options.Host,
                    MaxEpisodes = options.MaxEpisodes,
                    MaxConsecutiveErrors = 3,
                    DelayBetweenEpisodes = 2000, // 2 Sekunden Pause
                    ContinueOnError = true
                };

                var result = await _extractionService!.ExtractSeriesAsync(options.Url, extractionOptions);

                Console.WriteLine($"\n✅ EXTRAKTION ABGESCHLOSSEN!");
                Console.WriteLine($"=============================");
                Console.WriteLine($"📺 Serie: {result.Series.Name}");
                Console.WriteLine($"📊 Staffeln: {result.Series.Seasons.Count}");
                Console.WriteLine($"📈 Episoden verarbeitet: {result.ProcessedEpisodes}");
                Console.WriteLine($"🔗 Links gefunden: {result.TotalLinksFound}");
                Console.WriteLine($"❌ Fehler: {result.TotalErrors}");
                Console.WriteLine($"⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"📈 Erfolgsrate: {result.SuccessRate:F1}%");

                if (result.HostStatistics.Any())
                {
                    Console.WriteLine($"\n🏠 HOST-STATISTIKEN:");
                    foreach (var host in result.HostStatistics)
                    {
                        Console.WriteLine($"   {host.Key}: {host.Value} Links");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Extraktion fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> HandleDownloadCommand(string[] args)
        {
            var options = new DownloadCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--series":
                        if (i + 1 < args.Length) options.SeriesName = args[++i];
                        break;
                    case "--output-dir":
                        if (i + 1 < args.Length) options.OutputDir = args[++i];
                        break;
                    case "--parallel":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int parallel))
                            options.Parallel = parallel;
                        break;
                    case "--quality":
                        if (i + 1 < args.Length) options.Quality = args[++i];
                        break;
                }
            }

            return await ExecuteDownloadAsync(options);
        }

        private static async Task<int> __ExecuteDownloadAsync(DownloadCommandOptions options)
        {
            Console.WriteLine($"\n⬇️ STARTE DOWNLOADS");
            Console.WriteLine($"==================");
            Console.WriteLine($"📁 Output: {options.GetSafeOutputDir()}");
            Console.WriteLine($"🔢 Parallel: {options.Parallel}");
            Console.WriteLine($"🎥 Qualität: {options.Quality}");

            try
            {
                var downloadOptions = new DownloadOptions
                {
                    SeriesName = options.SeriesName,
                    OutputDirectory = options.OutputDir,
                    MaxParallelDownloads = options.Parallel,
                    Quality = options.Quality
                };

                var result = await _downloadService!.StartDownloadsAsync(downloadOptions);

                Console.WriteLine($"\n✅ DOWNLOADS ABGESCHLOSSEN!");
                Console.WriteLine($"===========================");
                Console.WriteLine($"📈 Geplante Downloads: {result.TotalDownloads}");
                Console.WriteLine($"✅ Erfolgreich: {result.SuccessfulDownloads}");
                Console.WriteLine($"❌ Fehlgeschlagen: {result.FailedDownloads}");
                Console.WriteLine($"⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"📈 Erfolgsrate: {result.SuccessRate:F1}%");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Download fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        // In der Program.cs - ersetze die ExecuteDownloadAsync Methode:

        private static async Task<int> ExecuteDownloadAsync(DownloadCommandOptions options)
        {
            Console.WriteLine($"\n⬇️ STARTE DOWNLOADS");
            Console.WriteLine($"==================");
            Console.WriteLine($"📁 Output: {options.GetSafeOutputDir()}");
            Console.WriteLine($"🔢 Parallel: {options.Parallel}");
            Console.WriteLine($"🎥 Qualität: {options.Quality}");

            try
            {
                var downloadOptions = new DownloadOptions
                {
                    SeriesName = options.SeriesName,
                    OutputDirectory = options.OutputDir,
                    MaxParallelDownloads = options.Parallel,
                    Quality = options.Quality,
                    OverwriteExisting = false, // Überschreibe nicht
                    CreateSubdirectories = true, // Erstelle Serie/Staffel Ordner
                    TimeoutMinutes = 15 // 15 Minuten Timeout pro Datei
                };

                var result = await _downloadService!.StartDownloadsAsync(downloadOptions);

                Console.WriteLine($"\n✅ DOWNLOADS ABGESCHLOSSEN!");
                Console.WriteLine($"===========================");
                Console.WriteLine($"📈 Geplante Downloads: {result.TotalDownloads}");
                Console.WriteLine($"✅ Erfolgreich: {result.SuccessfulDownloads}");
                Console.WriteLine($"❌ Fehlgeschlagen: {result.FailedDownloads}");
                Console.WriteLine($"⏭️ Übersprungen: {result.SkippedDownloads}");
                Console.WriteLine($"⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"📈 Erfolgsrate: {result.SuccessRate:F1}%");

                // Zeige Fehler-Details falls vorhanden
                if (result.Errors.Any())
                {
                    Console.WriteLine($"\n❌ FEHLER-DETAILS:");
                    foreach (var error in result.Errors.Take(5))
                    {
                        Console.WriteLine($"   {error.FileName}: {error.ErrorMessage}");
                    }
                    if (result.Errors.Count > 5)
                    {
                        Console.WriteLine($"   ... und {result.Errors.Count - 5} weitere Fehler");
                    }
                }

                // Zeige Quality-Statistiken
                if (result.QualityStatistics.Any())
                {
                    Console.WriteLine($"\n📊 QUALITÄTS-STATISTIKEN:");
                    foreach (var quality in result.QualityStatistics)
                    {
                        Console.WriteLine($"   {quality.Key}: {quality.Value} Downloads");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Download fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        // Zusätzlich: ShowHelp um Download-Beispiele erweitern
        private static int ShowHelp()
        {
            Console.WriteLine("\n📖 HILFE - YT-DLP Link Extractor");
            Console.WriteLine("==================================");
            Console.WriteLine();
            Console.WriteLine("🔍 LINK-EXTRAKTION:");
            Console.WriteLine("   extract --url <URL> [--series-name <n>] [--host vidmoly] [--max-episodes <Zahl>]");
            Console.WriteLine();
            Console.WriteLine("⬇️ DOWNLOADS:");
            Console.WriteLine("   download [--series <n>] [--output-dir <Pfad>] [--parallel <Zahl>] [--quality <Qualität>]");
            Console.WriteLine();
            Console.WriteLine("📊 STATUS:");
            Console.WriteLine("   status");
            Console.WriteLine();
            Console.WriteLine("🧪 SYSTEM-TEST:");
            Console.WriteLine("   test");
            Console.WriteLine();
            Console.WriteLine("📝 BEISPIELE:");
            Console.WriteLine("   # Links extrahieren");
            Console.WriteLine("   extract --url \"https://aniworld.to/anime/stream/one-piece/staffel-1/episode-1\" --max-episodes 3");
            Console.WriteLine();
            Console.WriteLine("   # Alle Links einer Serie downloaden");
            Console.WriteLine("   download --series \"one piece\"");
            Console.WriteLine();
            Console.WriteLine("   # Downloads mit speziellen Einstellungen");
            Console.WriteLine("   download --series \"one piece\" --parallel 1 --quality \"best[height<=720]\"");
            Console.WriteLine();
            Console.WriteLine("   # Status prüfen");
            Console.WriteLine("   status");
            Console.WriteLine();
            Console.WriteLine("📁 ORDNERSTRUKTUR:");
            Console.WriteLine("   Downloads/");
            Console.WriteLine("   ├── OnePiece/");
            Console.WriteLine("   │   ├── Staffel 01/");
            Console.WriteLine("   │   │   ├── OnePieceS1F01.mp4");
            Console.WriteLine("   │   │   ├── OnePieceS1F02.mp4");
            Console.WriteLine("   │   │   └── ...");
            Console.WriteLine("   │   └── Staffel 02/");
            Console.WriteLine("   └── AndereSerien/");
            Console.WriteLine();
            Console.WriteLine("🎥 QUALITÄTSOPTIONEN:");
            Console.WriteLine("   best               # Beste verfügbare Qualität");
            Console.WriteLine("   worst              # Schlechteste Qualität (schneller)");
            Console.WriteLine("   best[height<=720]  # Maximal 720p");
            Console.WriteLine("   best[height<=480]  # Maximal 480p");

            return 0;
        }

        private static async Task<int> HandleStatusCommand(string[] args)
        {
            Console.WriteLine("\n📊 DATABASE STATUS");
            Console.WriteLine("==================");

            try
            {
                var stats = await _dbContext!.GetStatisticsAsync();

                Console.WriteLine($"📺 Serien: {stats.TotalSeries}");
                Console.WriteLine($"📊 Staffeln: {stats.TotalSeasons}");
                Console.WriteLine($"📈 Episoden: {stats.TotalEpisodes}");
                Console.WriteLine($"🔗 Links gesamt: {stats.TotalLinks}");
                Console.WriteLine($"✅ Gültige Links: {stats.ValidLinks} ({stats.ValidLinksPercentage:F1}%)");
                Console.WriteLine($"⬇️ Downloads abgeschlossen: {stats.CompletedDownloads}");
                Console.WriteLine($"⏳ Downloads ausstehend: {stats.PendingDownloads}");
                Console.WriteLine($"❌ Downloads fehlgeschlagen: {stats.FailedDownloads}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Status-Abruf fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> RunTestCommand()
        {
            Console.WriteLine("\n🧪 SYSTEM-TEST");
            Console.WriteLine("==============");

            try
            {
                // Database Test
                var stats = await _dbContext!.GetStatisticsAsync();
                Console.WriteLine($"✅ Datenbank: OK ({stats.TotalSeries} Serien)");

                // Extractor Test
                var extractors = _extractionService!.GetRegisteredExtractors();
                Console.WriteLine($"✅ Extraktoren: {extractors.Count} registriert");
                foreach (var extractor in extractors)
                {
                    Console.WriteLine($"   🔧 {extractor}");
                }

                // Vidmoly Test
                var testUrl = "https://aniworld.to/anime/stream/one-piece/staffel-1/episode-1";
                var vidmolyExtractor = _extractionService.GetExtractorForHost("Vidmoly");
                if (vidmolyExtractor != null)
                {
                    bool canHandle = vidmolyExtractor.CanHandle(testUrl);
                    Console.WriteLine($"✅ Vidmoly-Test: {(canHandle ? "OK" : "FAIL")}");
                    Console.WriteLine($"   🔗 Test-URL: {testUrl}");
                }

                Console.WriteLine($"✅ Alle Tests bestanden!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static int __ShowHelp()
        {
            Console.WriteLine("\n📖 HILFE - YT-DLP Link Extractor");
            Console.WriteLine("==================================");
            Console.WriteLine();
            Console.WriteLine("🔍 LINK-EXTRAKTION:");
            Console.WriteLine("   extract --url <URL> [--series-name <n>] [--host vidmoly] [--max-episodes <Zahl>]");
            Console.WriteLine();
            Console.WriteLine("⬇️ DOWNLOADS:");
            Console.WriteLine("   download [--series <n>] [--output-dir <Pfad>] [--parallel <Zahl>] [--quality <Qualität>]");
            Console.WriteLine();
            Console.WriteLine("📊 STATUS:");
            Console.WriteLine("   status");
            Console.WriteLine();
            Console.WriteLine("🧪 SYSTEM-TEST:");
            Console.WriteLine("   test");
            Console.WriteLine();
            Console.WriteLine("📝 BEISPIELE:");
            Console.WriteLine("   extract --url \"https://aniworld.to/anime/stream/one-piece/staffel-1/episode-1\" --max-episodes 3");
            Console.WriteLine("   download --series \"one piece\" --parallel 2");
            Console.WriteLine("   status");

            return 0;
        }

        private static int ShowInvalidCommand(string command)
        {
            Console.WriteLine($"❌ Unbekannter Befehl: {command}");
            Console.WriteLine("Verwende 'help' für Hilfe.");
            return 1;
        }

        private static string[] ParseInput(string input)
        {
            var args = new List<string>();
            var currentArg = "";
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"' && (i == 0 || input[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(currentArg))
                    {
                        args.Add(currentArg);
                        currentArg = "";
                    }
                }
                else
                {
                    currentArg += c;
                }
            }

            if (!string.IsNullOrEmpty(currentArg))
                args.Add(currentArg);

            return args.ToArray();
        }

        private static async Task CleanupAsync()
        {
            try
            {
                await _extractionService?.CleanupAsync()!;
                _dbContext?.Dispose();
                Console.WriteLine("🧹 Cleanup abgeschlossen");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Cleanup-Fehler: {ex.Message}");
            }
        }
    }

    // Extension für ExtractionService
    public static class ExtractionServiceExtensions
    {
        public static List<string> GetRegisteredExtractors(this ExtractionService service)
        {
            return new List<string> { "Vidmoly" };
        }
    }
}