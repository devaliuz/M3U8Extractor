using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using YtDlpExtractor.Core.Database;
using YtDlpExtractor.Core.Models;
using YtDlpExtractor.Configuration;

namespace YtDlpExtractor.Services
{
    public class DownloadService
    {
        private readonly DatabaseContext _dbContext;
        private readonly string _ytDlpPath;

        public DownloadService(DatabaseContext dbContext, string? ytDlpPath = null)
        {
            _dbContext = dbContext;

            // Suche yt-dlp im Ausführungsordner, dann im PATH
            _ytDlpPath = FindYtDlpPath(ytDlpPath);
        }

        private string FindYtDlpPath(string? providedPath)
        {
            if (!string.IsNullOrEmpty(providedPath) && File.Exists(providedPath))
                return providedPath;

            // 1. Im aktuellen Verzeichnis suchen
            var currentDir = Directory.GetCurrentDirectory();
            var localPaths = new[]
            {
                Path.Combine(currentDir, "yt-dlp.exe"),
                Path.Combine(currentDir, "yt-dlp"),
                Path.Combine(currentDir, "bin", "yt-dlp.exe"),
                Path.Combine(currentDir, "bin", "yt-dlp")
            };

            foreach (var path in localPaths)
            {
                if (File.Exists(path))
                {
                    Console.WriteLine($"✅ yt-dlp gefunden: {path}");
                    return path;
                }
            }

            // 2. Fallback: System PATH
            Console.WriteLine("⚠️ yt-dlp nicht lokal gefunden, verwende System PATH");
            return "yt-dlp";
        }

        public async Task<DownloadResult> StartDownloadsAsync(DownloadOptions options)
        {
            var result = new DownloadResult();

            try
            {
                // 1. Links aus Datenbank laden
                var links = await GetLinksForDownloadAsync(options);
                result.TotalDownloads = links.Count;

                if (links.Count == 0)
                {
                    Console.WriteLine("📭 Keine Downloads gefunden");
                    result.Complete();
                    return result;
                }

                Console.WriteLine($"📥 {links.Count} Downloads geplant");
                Console.WriteLine($"🔧 Verwende yt-dlp: {_ytDlpPath}");

                // 2. Basis-Output-Verzeichnis erstellen
                Directory.CreateDirectory(options.OutputDirectory);

                // 3. Downloads starten (mit Parallelisierung)
                var semaphore = new SemaphoreSlim(options.MaxParallelDownloads);
                var downloadTasks = links.Select(link => DownloadSingleAsync(link, options, semaphore, result));

                await Task.WhenAll(downloadTasks);

                result.Complete();
                Console.WriteLine($"\n✅ Downloads abgeschlossen!");
                Console.WriteLine($"   ✅ Erfolgreich: {result.SuccessfulDownloads}");
                Console.WriteLine($"   ❌ Fehlgeschlagen: {result.FailedDownloads}");
                Console.WriteLine($"   ⏭️ Übersprungen: {result.SkippedDownloads}");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Download-Session fehlgeschlagen: {ex.Message}");
                throw;
            }
        }

        private async Task<List<DownloadableLink>> GetLinksForDownloadAsync(DownloadOptions options)
        {
            var query = _dbContext.Links
                .Include(l => l.Episode)
                    .ThenInclude(e => e.Season)
                        .ThenInclude(s => s.Series)
                .Where(l => l.IsValid && l.DownloadStatus == DownloadStatus.NotStarted);

            if (!string.IsNullOrEmpty(options.SeriesName))
            {
                query = query.Where(l => l.Episode.Season.Series.Name == options.SeriesName ||
                                        l.Episode.Season.Series.CleanName == options.SeriesName);
            }

            if (options.Season.HasValue)
            {
                query = query.Where(l => l.Episode.Season.Number == options.Season.Value);
            }

            if (options.Episode.HasValue)
            {
                query = query.Where(l => l.Episode.Number == options.Episode.Value);
            }

            return await query
                .OrderBy(l => l.Episode.Season.Series.Name)
                .ThenBy(l => l.Episode.Season.Number)
                .ThenBy(l => l.Episode.Number)
                .ToListAsync();
        }

        private async Task DownloadSingleAsync(DownloadableLink link, DownloadOptions options,
            SemaphoreSlim semaphore, DownloadResult result)
        {
            await semaphore.WaitAsync();

            try
            {
                var episode = link.Episode;
                var season = episode.Season;
                var series = season.Series;

                // Erstelle Ordnerstruktur: Serie -> Staffel
                var seriesDir = CreateSeriesDirectory(options.OutputDirectory, series.Name);
                var seasonDir = CreateSeasonDirectory(seriesDir, season.Number);

                // Generiere finalen Dateinamen: OnePieceS1F1
                var finalFileName = GenerateFinalFileName(series.Name, season.Number, episode.Number);
                var finalFilePath = Path.Combine(seasonDir, $"{finalFileName}.mp4");

                // Prüfe ob Datei bereits existiert
                if (File.Exists(finalFilePath) && !options.OverwriteExisting)
                {
                    Console.WriteLine($"⏭️ Überspringe (existiert): {finalFileName}");
                    result.SkippedDownloads++;
                    link.UpdateDownloadStatus(DownloadStatus.Completed, null, finalFilePath);
                    await _dbContext.SaveChangesAsync();
                    return;
                }

                Console.WriteLine($"⬇️ Download: {finalFileName}");

                // Update Status zu "Downloading"
                link.UpdateDownloadStatus(DownloadStatus.Downloading);
                await _dbContext.SaveChangesAsync();

                // Temp-Datei für yt-dlp Download
                var tempFileName = $"temp_{Guid.NewGuid():N}";
                var tempOutputPattern = Path.Combine(seasonDir, $"{tempFileName}.%(ext)s");

                // yt-dlp ausführen
                var success = await ExecuteYtDlpAsync(link.Url, tempOutputPattern, options.Quality);

                if (success)
                {
                    // Finde heruntergeladene Datei und benenne sie um
                    var downloadedFile = FindDownloadedFile(seasonDir, tempFileName);
                    if (!string.IsNullOrEmpty(downloadedFile))
                    {
                        // Verschiebe/Benenne um zur finalen Datei
                        File.Move(downloadedFile, finalFilePath);

                        link.UpdateDownloadStatus(DownloadStatus.Completed, null, finalFilePath);
                        result.SuccessfulDownloads++;
                        result.AddSuccess(options.Quality);

                        Console.WriteLine($"✅ Fertig: {finalFileName}");
                    }
                    else
                    {
                        link.UpdateDownloadStatus(DownloadStatus.Failed, "Heruntergeladene Datei nicht gefunden");
                        result.AddError(finalFileName, "Datei nach Download nicht gefunden");
                        Console.WriteLine($"❌ Datei nicht gefunden: {finalFileName}");
                    }
                }
                else
                {
                    link.UpdateDownloadStatus(DownloadStatus.Failed, "yt-dlp Download fehlgeschlagen");
                    result.AddError(finalFileName, "yt-dlp fehlgeschlagen");
                    Console.WriteLine($"❌ Download fehlgeschlagen: {finalFileName}");
                }

                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var fileName = GenerateFinalFileName(
                    link.Episode.Season.Series.Name,
                    link.Episode.Season.Number,
                    link.Episode.Number);

                link.UpdateDownloadStatus(DownloadStatus.Failed, ex.Message);
                await _dbContext.SaveChangesAsync();
                result.AddError(fileName, ex.Message);
                Console.WriteLine($"❌ Fehler bei {fileName}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        private string CreateSeriesDirectory(string baseDir, string seriesName)
        {
            var cleanSeriesName = CleanDirectoryName(seriesName);
            var seriesDir = Path.Combine(baseDir, cleanSeriesName);
            Directory.CreateDirectory(seriesDir);
            return seriesDir;
        }

        private string CreateSeasonDirectory(string seriesDir, int seasonNumber)
        {
            var seasonDirName = $"Staffel {seasonNumber:D2}";
            var seasonDir = Path.Combine(seriesDir, seasonDirName);
            Directory.CreateDirectory(seasonDir);
            return seasonDir;
        }

        private string GenerateFinalFileName(string seriesName, int seasonNumber, int episodeNumber)
        {
            var cleanSeriesName = CleanFileName(seriesName);
            return $"{cleanSeriesName}S{seasonNumber}F{episodeNumber:D2}";
        }

        private string CleanDirectoryName(string name)
        {
            // Für Ordnernamen - etwas weniger restriktiv
            var invalidChars = Path.GetInvalidPathChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }).ToArray();
            var cleaned = string.Join("", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return cleaned.Trim().Replace("  ", " ");
        }

        private string CleanFileName(string name)
        {
            // Für Dateinamen - restriktiver
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = string.Join("", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return cleaned.Trim().Replace(" ", "").Replace("-", "");
        }

        private async Task<bool> ExecuteYtDlpAsync(string url, string outputPattern, string quality)
        {
            try
            {
                var arguments = BuildYtDlpArguments(url, outputPattern, quality);

                var processInfo = new ProcessStartInfo(_ytDlpPath, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };

                Console.WriteLine($"   🔧 Befehl: {_ytDlpPath} {arguments}");

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine($"   ❌ Prozess konnte nicht gestartet werden");
                    return false;
                }

                // Lese Output für Debugging
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode == 0)
                {
                    Console.WriteLine($"   ✅ yt-dlp erfolgreich");
                    return true;
                }
                else
                {
                    Console.WriteLine($"   ❌ yt-dlp Fehler (Exit Code: {process.ExitCode})");
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"   📝 Error: {error.Split('\n')[0]}"); // Nur erste Zeile
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ yt-dlp Ausführungsfehler: {ex.Message}");
                return false;
            }
        }

        private string BuildYtDlpArguments(string url, string outputPattern, string quality)
        {
            var args = new List<string>
            {
                $"\"{url}\"",
                "--output", $"\"{outputPattern}\"",
                "--format", $"\"{quality}\"",
                "--merge-output-format", "mp4",
                "--no-playlist",
                "--no-warnings",
                "--quiet"
            };

            return string.Join(" ", args);
        }

        private string? FindDownloadedFile(string directory, string tempFileName)
        {
            try
            {
                // Suche nach Dateien die mit dem Temp-Namen beginnen
                var files = Directory.GetFiles(directory, $"{tempFileName}.*")
                    .Where(f => !Path.GetExtension(f).Equals(".part", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count > 0)
                {
                    // Bevorzuge .mp4, dann andere Video-Formate
                    var mp4File = files.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
                    if (mp4File != null) return mp4File;

                    var videoExtensions = new[] { ".mkv", ".avi", ".mov", ".webm" };
                    var videoFile = files.FirstOrDefault(f => videoExtensions.Any(ext =>
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
                    if (videoFile != null) return videoFile;

                    // Fallback: Erste Datei
                    return files[0];
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Fehler beim Suchen der Datei: {ex.Message}");
                return null;
            }
        }
    }
}