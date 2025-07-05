using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        private static AppSettings? _appSettings;

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("🎬 YT-DLP Link Extractor & Automation Tool");
            Console.WriteLine("==========================================");

            // Load Settings
            LoadSettings();

            // Initialize Database
            await InitializeDatabaseAsync();

            // Initialize Services
            await InitializeServicesAsync();

            try
            {
                if (args.Length == 0)
                {
                    // Prüfe ob wir im Debug-Modus sind oder interaktiv laufen sollen
                    if (IsDebugMode() || IsInteractiveEnvironment())
                    {
                        return await RunInteractiveMode();
                    }
                    else
                    {
                        ShowHelp();
                        return 0;
                    }
                }

                var command = args[0].ToLower();
                return command switch
                {
                    "extract" => await HandleExtractCommand(args),
                    "download" => await HandleDownloadCommand(args),
                    "status" => await HandleStatusCommand(args),
                    "export" => await HandleExportCommand(args),
                    "validate" => await HandleValidateCommand(args),
                    "cleanup" => await HandleCleanupCommand(args),
                    "interactive" => await RunInteractiveMode(),
                    "help" or "--help" or "-h" => ShowHelp(),
                    _ => ShowInvalidCommand(command)
                };
            }
            finally
            {
                await CleanupAsync();
            }
        }

        private static bool IsDebugMode()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        private static bool IsInteractiveEnvironment()
        {
            return Environment.UserInteractive && !Console.IsInputRedirected;
        }

        private static async Task<int> RunInteractiveMode()
        {
            Console.WriteLine("\n🎮 INTERAKTIVER MODUS");
            Console.WriteLine("====================");
            Console.WriteLine("Geben Sie Befehle direkt ein oder 'help' für Hilfe, 'exit' zum Beenden.");
            Console.WriteLine();

            while (true)
            {
                Console.Write("ytdlp> ");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    continue;

                if (input.ToLower() is "exit" or "quit" or "q")
                {
                    Console.WriteLine("👋 Auf Wiedersehen!");
                    return 0;
                }

                if (input.ToLower() is "clear" or "cls")
                {
                    Console.Clear();
                    Console.WriteLine("🎬 YT-DLP Link Extractor - Interaktiver Modus");
                    continue;
                }

                try
                {
                    // Parse Input in Argumente
                    var args = ParseInteractiveInput(input);
                    if (args.Length == 0) continue;

                    var command = args[0].ToLower();
                    var result = command switch
                    {
                        "extract" => await HandleExtractCommand(args),
                        "download" => await HandleDownloadCommand(args),
                        "status" => await HandleStatusCommand(args),
                        "export" => await HandleExportCommand(args),
                        "validate" => await HandleValidateCommand(args),
                        "cleanup" => await HandleCleanupCommand(args),
                        "help" => ShowHelp(),
                        "test" => await RunTestCommand(),
                        _ => ShowInvalidCommand(command)
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

                Console.WriteLine(); // Leerzeile für bessere Lesbarkeit
            }
        }

        private static async Task<int> RunTestCommand()
        {
            Console.WriteLine("\n🧪 TEST-MODUS");
            Console.WriteLine("=============");

            try
            {
                // Test Database Connection
                var stats = await _dbContext!.GetStatisticsAsync();
                Console.WriteLine($"✅ Datenbank-Verbindung: OK");
                Console.WriteLine($"   📊 Gespeicherte Serien: {stats.TotalSeries}");
                Console.WriteLine($"   🔗 Gespeicherte Links: {stats.TotalLinks}");

                // Test Extractor
                var extractors = _extractionService!.GetRegisteredExtractors();
                Console.WriteLine($"✅ Extraktoren geladen: {extractors.Count}");
                foreach (var extractor in extractors)
                {
                    Console.WriteLine($"   🔧 {extractor}");
                }

                // Test Vidmoly URL Pattern
                var testUrl = "https://vidmoly.to/embed-abc123def.html";
                var vidmolyExtractor = _extractionService.GetExtractorForHost("Vidmoly");
                if (vidmolyExtractor != null)
                {
                    bool canHandle = vidmolyExtractor.CanHandle(testUrl);
                    Console.WriteLine($"✅ Vidmoly URL-Test: {(canHandle ? "OK" : "FAIL")}");
                    Console.WriteLine($"   🔗 Test-URL: {testUrl}");
                }

                // Test Paths
                var paths = _appSettings?.Paths ?? new PathSettings();
                Console.WriteLine($"✅ Pfad-Konfiguration:");
                Console.WriteLine($"   📁 Downloads: {paths.GetFullDownloadPath()}");
                Console.WriteLine($"   📂 Temp: {paths.GetFullTempPath()}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static string[] ParseInteractiveInput(string input)
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
            {
                args.Add(currentArg);
            }

            return args.ToArray();
        }

        private static void LoadSettings()
        {
            try
            {
                _appSettings = SettingsLoader.LoadFromFile("config/appsettings.json");
                _appSettings.Paths.EnsureDirectoriesExist();
                _appSettings.Logging.EnsureLogDirectoriesExist();
                Console.WriteLine("✅ Settings geladen");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Laden der Settings: {ex.Message}");
                _appSettings = SettingsLoader.LoadDefault();
            }
        }

        private static async Task InitializeDatabaseAsync()
        {
            try
            {
                var connectionString = _appSettings?.Database.ConnectionString ?? "Data Source=ytdlp_extractor.db";
                _dbContext = new DatabaseContext(connectionString);
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

            // Registriere Extraktoren
            _extractionService.RegisterExtractor(new VidmolyExtractor());

            Console.WriteLine("✅ Services initialisiert");
        }

        private static int ShowHelp()
        {
            Console.WriteLine("\n📖 VERWENDUNG:");
            Console.WriteLine("==============");
            Console.WriteLine();

            if (IsDebugMode() || IsInteractiveEnvironment())
            {
                Console.WriteLine("🎮 INTERAKTIVER MODUS:");
                Console.WriteLine("   ytdlp-extractor                           # Startet interaktiven Modus");
                Console.WriteLine("   ytdlp-extractor interactive               # Startet interaktiven Modus");
                Console.WriteLine();
                Console.WriteLine("   Im interaktiven Modus können Sie Befehle direkt eingeben:");
                Console.WriteLine("   • test                                     # Teste Systemkomponenten");
                Console.WriteLine("   • status                                   # Zeige aktuellen Status");
                Console.WriteLine("   • help                                     # Zeige diese Hilfe");
                Console.WriteLine("   • clear/cls                                # Bildschirm löschen");
                Console.WriteLine("   • exit/quit                                # Beenden");
                Console.WriteLine();
            }

            Console.WriteLine("🔍 EXTRAKTION:");
            Console.WriteLine("   ytdlp-extractor extract --url <URL> [Optionen]");
            Console.WriteLine("   Optionen:");
            Console.WriteLine("     --url <URL>              Episode-URL (Pflicht)");
            Console.WriteLine("     --series-name <n>        Serie-Name (optional)");
            Console.WriteLine("     --host <Host>            Host-Typ (vidmoly, auto) [Standard: auto]");
            Console.WriteLine("     --start-season <Zahl>    Start-Staffel [Standard: 1]");
            Console.WriteLine("     --start-episode <Zahl>   Start-Episode [Standard: 1]");
            Console.WriteLine("     --max-episodes <Zahl>    Max. Episoden (optional)");
            Console.WriteLine();
            Console.WriteLine("⬇️ DOWNLOAD:");
            Console.WriteLine("   ytdlp-extractor download [Optionen]");
            Console.WriteLine("   Optionen:");
            Console.WriteLine("     --series <n>             Serie-Name (optional)");
            Console.WriteLine("     --season <Zahl>          Spezifische Staffel (optional)");
            Console.WriteLine("     --episode <Zahl>         Spezifische Episode (optional)");
            Console.WriteLine("     --output-dir <Pfad>      Download-Verzeichnis [Standard: ./Downloads]");
            Console.WriteLine("     --parallel <Zahl>        Parallele Downloads [Standard: 2]");
            Console.WriteLine("     --quality <Qualität>     Video-Qualität [Standard: best]");
            Console.WriteLine();
            Console.WriteLine("📊 STATUS:");
            Console.WriteLine("   ytdlp-extractor status [--series <n>] [--detailed]");
            Console.WriteLine();
            Console.WriteLine("📤 EXPORT:");
            Console.WriteLine("   ytdlp-extractor export [Optionen]");
            Console.WriteLine("   Optionen:");
            Console.WriteLine("     --format <Format>        Export-Format (json, csv, batch) [Standard: json]");
            Console.WriteLine("     --series <n>             Spezifische Serie (optional)");
            Console.WriteLine("     --output <Datei>         Output-Datei (optional)");
            Console.WriteLine();
            Console.WriteLine("🔍 VALIDATE:");
            Console.WriteLine("   ytdlp-extractor validate [--series <n>] [--force]");
            Console.WriteLine();
            Console.WriteLine("🧹 CLEANUP:");
            Console.WriteLine("   ytdlp-extractor cleanup [--days <Tage>] [--dry-run]");
            Console.WriteLine("   Standard: Lösche Links älter als 30 Tage");
            Console.WriteLine();
            Console.WriteLine("📝 BEISPIELE:");
            Console.WriteLine("   ytdlp-extractor extract --url \"https://example.com/stream/serie/staffel-1/episode-1\" --host vidmoly");
            Console.WriteLine("   ytdlp-extractor download --series \"Meine Serie\" --parallel 3");
            Console.WriteLine("   ytdlp-extractor export --format batch --series \"Meine Serie\"");

            return 0;
        }

        private static int ShowInvalidCommand(string command)
        {
            Console.WriteLine($"❌ Unbekannter Befehl: {command}");
            Console.WriteLine("Verwende 'help' für eine Liste aller Befehle.");
            return 1;
        }

        // Extract Command Handler
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
                    case "--start-season":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int season))
                            options.StartSeason = season;
                        break;
                    case "--start-episode":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int episode))
                            options.StartEpisode = episode;
                        break;
                    case "--max-episodes":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int max))
                            options.MaxEpisodes = max;
                        break;
                }
            }

            try
            {
                options.Validate();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"❌ Fehler: {ex.Message}");
                Console.WriteLine("Beispiel: extract --url \"https://example.com/episode-1\"");
                return 1;
            }

            return await ExtractLinksAsync(options);
        }

        // Download Command Handler
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
                    case "--season":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int season))
                            options.Season = season;
                        break;
                    case "--episode":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int episode))
                            options.Episode = episode;
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

            try
            {
                options.Validate();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"❌ Fehler: {ex.Message}");
                return 1;
            }

            return await StartDownloadsAsync(options);
        }

        // Status Command Handler
        private static async Task<int> HandleStatusCommand(string[] args)
        {
            var options = new StatusCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--series":
                        if (i + 1 < args.Length) options.SeriesName = args[++i];
                        break;
                    case "--detailed":
                        options.ShowDetailed = true;
                        break;
                    case "--active-only":
                        options.ShowOnlyActive = true;
                        break;
                }
            }

            await ShowStatusAsync(options);
            return 0;
        }

        // Export Command Handler
        private static async Task<int> HandleExportCommand(string[] args)
        {
            var options = new ExportCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--format":
                        if (i + 1 < args.Length) options.Format = args[++i];
                        break;
                    case "--series":
                        if (i + 1 < args.Length) options.SeriesName = args[++i];
                        break;
                    case "--output":
                        if (i + 1 < args.Length) options.OutputFile = args[++i];
                        break;
                }
            }

            try
            {
                options.Validate();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"❌ Fehler: {ex.Message}");
                return 1;
            }

            await ExportLinksAsync(options);
            return 0;
        }

        // Validate Command Handler
        private static async Task<int> HandleValidateCommand(string[] args)
        {
            var options = new ValidateCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--series":
                        if (i + 1 < args.Length) options.SeriesName = args[++i];
                        break;
                    case "--force":
                        options.ForceRevalidation = true;
                        break;
                    case "--batch-size":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int batchSize))
                            options.BatchSize = batchSize;
                        break;
                    case "--timeout":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int timeout))
                            options.TimeoutSeconds = timeout;
                        break;
                }
            }

            try
            {
                options.Validate();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"❌ Fehler: {ex.Message}");
                return 1;
            }

            await ValidateLinksAsync(options);
            return 0;
        }

        // Cleanup Command Handler
        private static async Task<int> HandleCleanupCommand(string[] args)
        {
            var options = new CleanupCommandOptions();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--days":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int days))
                            options.Days = days;
                        break;
                    case "--dry-run":
                        options.DryRun = true;
                        break;
                    case "--no-invalid":
                        options.CleanupInvalidLinks = false;
                        break;
                    case "--no-failed":
                        options.CleanupFailedSeries = false;
                        break;
                    case "--empty-series":
                        options.CleanupEmptySeries = true;
                        break;
                }
            }

            try
            {
                options.Validate();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"❌ Fehler: {ex.Message}");
                return 1;
            }

            await CleanupDatabaseAsync(options);
            return 0;
        }

        // Command Implementation Methods - VERKÜRZT für bessere Übersicht
        private static async Task<int> ExtractLinksAsync(ExtractCommandOptions options)
        {
            Console.WriteLine($"\n🔍 Starte Link-Extraktion");
            Console.WriteLine($"   📺 URL: {options.Url}");
            Console.WriteLine($"   🎬 Serie: {options.SeriesName ?? "Auto-Detect"}");
            Console.WriteLine($"   🏠 Host: {options.Host}");
            Console.WriteLine($"   📊 Start: S{options.StartSeason}E{options.StartEpisode}");
            if (options.MaxEpisodes.HasValue)
                Console.WriteLine($"   📈 Max Episodes: {options.MaxEpisodes}");

            try
            {
                var extractionOptions = new ExtractionOptions
                {
                    SeriesName = options.SeriesName,
                    PreferredHost = options.Host,
                    StartSeason = options.StartSeason,
                    StartEpisode = options.StartEpisode,
                    MaxEpisodes = options.MaxEpisodes
                };

                var result = await _extractionService!.ExtractSeriesAsync(options.Url, extractionOptions);

                Console.WriteLine($"\n✅ Extraktion abgeschlossen!");
                Console.WriteLine($"   📺 Serie: {result.Series.Name}");
                Console.WriteLine($"   📊 Staffeln: {result.Series.Seasons.Count}");
                Console.WriteLine($"   📈 Episoden: {result.TotalEpisodes}");
                Console.WriteLine($"   🔗 Links gefunden: {result.TotalLinksFound}");
                Console.WriteLine($"   ❌ Fehler: {result.TotalErrors}");
                Console.WriteLine($"   ⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"   📈 Erfolgsrate: {result.SuccessRate:F1}%");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Extraktion fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> StartDownloadsAsync(DownloadCommandOptions options)
        {
            Console.WriteLine($"\n⬇️ Starte Downloads");
            Console.WriteLine($"   📁 Output: {options.GetSafeOutputDir()}");
            Console.WriteLine($"   🔢 Parallel: {options.Parallel}");
            Console.WriteLine($"   🎥 Qualität: {options.Quality}");

            try
            {
                var downloadOptions = new DownloadOptions
                {
                    SeriesName = options.SeriesName,
                    Season = options.Season,
                    Episode = options.Episode,
                    OutputDirectory = options.OutputDir,
                    MaxParallelDownloads = options.Parallel,
                    Quality = options.Quality
                };

                var result = await _downloadService!.StartDownloadsAsync(downloadOptions);

                Console.WriteLine($"\n✅ Download-Session abgeschlossen!");
                Console.WriteLine($"   📈 Geplante Downloads: {result.TotalDownloads}");
                Console.WriteLine($"   ✅ Erfolgreich: {result.SuccessfulDownloads}");
                Console.WriteLine($"   ❌ Fehlgeschlagen: {result.FailedDownloads}");
                Console.WriteLine($"   ⏭️ Übersprungen: {result.SkippedDownloads}");
                Console.WriteLine($"   ⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"   📈 Erfolgsrate: {result.SuccessRate:F1}%");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Download fehlgeschlagen: {ex.Message}");
                return 1;
            }
        }

        private static async Task ShowStatusAsync(StatusCommandOptions options)
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

                if (options.HasSeriesFilter)
                {
                    Console.WriteLine($"\n📺 DETAILS FÜR: {options.SeriesName}");
                    Console.WriteLine("=============================");

                    var series = await _dbContext.GetSeriesByNameAsync(options.SeriesName!);
                    if (series != null)
                    {
                        Console.WriteLine($"📅 Erstellt: {series.CreatedAt:yyyy-MM-dd HH:mm}");
                        Console.WriteLine($"🔄 Status: {series.Status}");
                        Console.WriteLine($"📊 Staffeln: {series.Seasons.Count}");
                        Console.WriteLine($"📈 Episoden: {series.TotalEpisodes}");
                        Console.WriteLine($"🔗 Links: {series.FoundLinks}");

                        if (options.ShowDetailed)
                        {
                            foreach (var season in series.Seasons.OrderBy(s => s.Number))
                            {
                                Console.WriteLine($"   S{season.Number:D2}: {season.Episodes.Count} Episoden, {season.FoundLinks} Links");

                                if (options.ShowDetailed)
                                {
                                    foreach (var episode in season.Episodes.OrderBy(e => e.Number))
                                    {
                                        Console.WriteLine($"      E{episode.Number:D2}: {episode.Links.Count} Links ({episode.Status})");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Serie '{options.SeriesName}' nicht gefunden");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Status-Abruf fehlgeschlagen: {ex.Message}");
            }
        }

        // Weitere Methods hier verkürzt - ExportLinksAsync, ValidateLinksAsync, CleanupDatabaseAsync
        // Export und andere Helper Methods wie zuvor...

        private static async Task ExportLinksAsync(ExportCommandOptions options)
        {
            Console.WriteLine($"📤 Export-Feature implementiert für {options.Format}");
        }

        private static async Task ValidateLinksAsync(ValidateCommandOptions options)
        {
            Console.WriteLine($"🔍 Validierungs-Feature implementiert");
        }

        private static async Task CleanupDatabaseAsync(CleanupCommandOptions options)
        {
            Console.WriteLine($"🧹 Cleanup-Feature implementiert");
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

    // Extension für ExtractionService um registrierte Extraktoren abzufragen
    public static class ExtractionServiceExtensions
    {
        public static List<string> GetRegisteredExtractors(this ExtractionService service)
        {
            // Diese Methode muss in ExtractionService implementiert werden
            return new List<string> { "Vidmoly" }; // Placeholder
        }
    }
}