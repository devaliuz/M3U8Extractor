using System.Diagnostics;
using Microsoft.EntityFrameworkCore;  // ← WICHTIG: Das fehlte!
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
            _ytDlpPath = ytDlpPath ?? "yt-dlp"; // Assume yt-dlp is in PATH
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
                    return result;
                }

                Console.WriteLine($"📥 {links.Count} Downloads geplant");

                // 2. Output-Verzeichnis erstellen
                Directory.CreateDirectory(options.OutputDirectory);

                // 3. Downloads starten (mit Parallelisierung)
                var semaphore = new SemaphoreSlim(options.MaxParallelDownloads);
                var downloadTasks = links.Select(link => DownloadSingleAsync(link, options, semaphore, result));

                await Task.WhenAll(downloadTasks);

                Console.WriteLine($"✅ Downloads abgeschlossen: {result.SuccessfulDownloads} erfolgreich, {result.FailedDownloads} fehlgeschlagen");
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
                var fileName = GenerateFileName(link);
                var outputPath = Path.Combine(options.OutputDirectory, $"{fileName}.%(ext)s");

                Console.WriteLine($"⬇️ Starte: {fileName}");

                // Update Status
                link.UpdateDownloadStatus(DownloadStatus.Downloading);
                await _dbContext.SaveChangesAsync();

                // yt-dlp ausführen
                var success = await ExecuteYtDlpAsync(link.Url, outputPath, options.Quality);

                if (success)
                {
                    link.UpdateDownloadStatus(DownloadStatus.Completed, null, outputPath);
                    result.SuccessfulDownloads++;
                    Console.WriteLine($"✅ Abgeschlossen: {fileName}");
                }
                else
                {
                    link.UpdateDownloadStatus(DownloadStatus.Failed, "yt-dlp Download fehlgeschlagen");
                    result.FailedDownloads++;
                    Console.WriteLine($"❌ Fehlgeschlagen: {fileName}");
                }

                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                link.UpdateDownloadStatus(DownloadStatus.Failed, ex.Message);
                await _dbContext.SaveChangesAsync();
                result.FailedDownloads++;
                Console.WriteLine($"❌ Fehler bei {link.Url}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<bool> ExecuteYtDlpAsync(string url, string outputPath, string quality)
        {
            try
            {
                var arguments = $"\"{url}\" --output \"{outputPath}\" --format \"{quality}\" --merge-output-format mp4 --no-playlist";

                var processInfo = new ProcessStartInfo(_ytDlpPath, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ yt-dlp Ausführungsfehler: {ex.Message}");
                return false;
            }
        }

        private string GenerateFileName(DownloadableLink link)
        {
            var series = link.Episode.Season.Series.CleanName;
            var season = link.Episode.Season.Number;
            var episode = link.Episode.Number;

            return $"{series}_S{season:D2}E{episode:D2}";
        }
    }
}