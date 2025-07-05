using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using YtDlpExtractor.Core.Database;
using YtDlpExtractor.Core.Interfaces;
using YtDlpExtractor.Core.Models;
using YtDlpExtractor.Configuration;

namespace YtDlpExtractor.Services
{
    public class ExtractionService
    {
        private readonly DatabaseContext _dbContext;
        private readonly Dictionary<string, IHostExtractor> _extractors;
        private readonly List<IHostExtractor> _allExtractors;

        public ExtractionService(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
            _extractors = new Dictionary<string, IHostExtractor>();
            _allExtractors = new List<IHostExtractor>();
        }

        public void RegisterExtractor(IHostExtractor extractor)
        {
            _extractors[extractor.HostName.ToLower()] = extractor;
            _allExtractors.Add(extractor);
            Console.WriteLine($"📝 Extraktor registriert: {extractor.HostName}");
        }

        public IHostExtractor? GetExtractorForHost(string hostName)
        {
            return _extractors.GetValueOrDefault(hostName.ToLower());
        }

        public List<string> GetRegisteredExtractors()
        {
            return _allExtractors.Select(e => e.HostName).ToList();
        }

        public async Task<ExtractionResult> ExtractSeriesAsync(string startUrl, ExtractionOptions options)
        {
            var result = new ExtractionResult { StartUrl = startUrl };
            result.StartedAt = DateTime.UtcNow;

            try
            {
                Console.WriteLine($"🎬 Starte Serie-Extraktion für: {startUrl}");
                Console.WriteLine($"   🎯 Host: {options.PreferredHost}");
                Console.WriteLine($"   📊 Start: S{options.StartSeason}E{options.StartEpisode}");
                if (options.MaxEpisodes.HasValue)
                    Console.WriteLine($"   📈 Max Episoden: {options.MaxEpisodes}");

                // 1. Serie erstellen oder laden
                var series = await GetOrCreateSeriesAsync(startUrl, options.SeriesName);
                result.Series = series;
                series.UpdateStatus(MediaStatus.Processing);
                await _dbContext.SaveChangesAsync();

                // 2. Passenden Extraktor finden
                var extractor = FindBestExtractor(startUrl, options.PreferredHost);
                if (extractor == null)
                {
                    throw new InvalidOperationException($"Kein passender Extraktor für {startUrl} gefunden");
                }

                Console.WriteLine($"🔧 Verwende Extraktor: {extractor.HostName}");

                // 3. Extraktor initialisieren
                if (!await extractor.InitializeAsync())
                {
                    throw new InvalidOperationException($"Extraktor {extractor.HostName} konnte nicht initialisiert werden");
                }

                try
                {
                    // 4. Automatische Episode-Extraktion durchführen
                    await PerformAutomaticSeriesExtractionAsync(extractor, series, startUrl, options, result);
                }
                finally
                {
                    // 5. Extraktor bereinigen
                    await extractor.CleanupAsync();
                }

                // 6. Serie als abgeschlossen markieren
                series.UpdateStatus(MediaStatus.Completed);
                result.Complete();
                await _dbContext.SaveChangesAsync();

                Console.WriteLine($"\n🎉 SERIE-EXTRAKTION ABGESCHLOSSEN!");
                Console.WriteLine($"   📺 Serie: {result.Series.Name}");
                Console.WriteLine($"   📊 Staffeln: {result.Series.Seasons.Count}");
                Console.WriteLine($"   📈 Episoden verarbeitet: {result.ProcessedEpisodes}");
                Console.WriteLine($"   🔗 Links gefunden: {result.TotalLinksFound}");
                Console.WriteLine($"   ❌ Fehler: {result.TotalErrors}");
                Console.WriteLine($"   ⏱️ Dauer: {result.Duration:mm\\:ss}");
                Console.WriteLine($"   📈 Erfolgsrate: {result.SuccessRate:F1}%");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Serie-Extraktion fehlgeschlagen: {ex.Message}");
                result.AddError($"Kritischer Fehler: {ex.Message}");
                result.Series?.UpdateStatus(MediaStatus.Failed, ex.Message);

                if (result.Series != null && _dbContext.Entry(result.Series).State != EntityState.Detached)
                {
                    await _dbContext.SaveChangesAsync();
                }
                throw;
            }
        }

        /// <summary>
        /// Automatische Extraktion einer kompletten Serie
        /// </summary>
        private async Task PerformAutomaticSeriesExtractionAsync(IHostExtractor extractor, Series series,
            string startUrl, ExtractionOptions options, ExtractionResult result)
        {
            var currentUrl = startUrl;
            var currentSeason = options.StartSeason;
            var currentEpisode = options.StartEpisode;
            var consecutiveErrors = 0;
            var totalProcessed = 0;

            Console.WriteLine($"\n🚀 STARTE AUTOMATISCHE SERIE-EXTRAKTION");
            Console.WriteLine($"========================================");

            while (consecutiveErrors < options.MaxConsecutiveErrors &&
                   (!options.MaxEpisodes.HasValue || totalProcessed < options.MaxEpisodes.Value))
            {
                try
                {
                    // Aktualisiere Season/Episode aus URL falls nötig
                    var detectedSeason = GetSeasonFromUrl(currentUrl);
                    var detectedEpisode = GetEpisodeFromUrl(currentUrl);

                    if (detectedSeason > 0) currentSeason = detectedSeason;
                    if (detectedEpisode > 0) currentEpisode = detectedEpisode;

                    Console.WriteLine($"\n📺 EPISODE S{currentSeason:D2}E{currentEpisode:D2}");
                    Console.WriteLine($"🔗 URL: {currentUrl}");

                    // Prüfe ob Episode bereits existiert und überspringe falls gewünscht
                    if (options.SkipExistingEpisodes)
                    {
                        var existingEpisode = await GetExistingEpisodeAsync(series, currentSeason, currentEpisode);
                        if (existingEpisode != null && existingEpisode.Links.Any())
                        {
                            Console.WriteLine($"⏭️ Episode bereits vorhanden, überspringe...");

                            // Nächste Episode finden
                            currentUrl = await GetNextEpisodeUrlAsync(extractor, currentUrl, currentSeason, currentEpisode);
                            if (currentUrl == null) break;

                            currentEpisode++;
                            continue;
                        }
                    }

                    // Staffel und Episode erstellen oder laden
                    var season = await GetOrCreateSeasonAsync(series, currentSeason);
                    var episode = await GetOrCreateEpisodeAsync(season, currentEpisode, currentUrl);

                    // Episode-Status aktualisieren
                    episode.UpdateStatus(EpisodeStatus.Processing);
                    await _dbContext.SaveChangesAsync();

                    // Links extrahieren
                    var startTime = DateTime.UtcNow;
                    var foundLinks = await extractor.ExtractLinksAsync(currentUrl, episode);
                    var extractionTime = DateTime.UtcNow - startTime;

                    if (foundLinks.Count > 0)
                    {
                        // Links zur Episode hinzufügen (Duplikate vermeiden)
                        var newLinksCount = 0;
                        foreach (var link in foundLinks)
                        {
                            if (!episode.Links.Any(l => l.Url == link.Url))
                            {
                                episode.Links.Add(link);
                                result.AddFoundLink(link.HostName);
                                newLinksCount++;
                            }
                        }

                        episode.UpdateStatus(EpisodeStatus.LinksFound);
                        consecutiveErrors = 0; // Reset error counter

                        Console.WriteLine($"✅ {newLinksCount} neue Links gefunden (⏱️ {extractionTime.TotalSeconds:F1}s)");

                        // Zeige gefundene Links
                        foreach (var link in foundLinks.Take(3)) // Nur erste 3 anzeigen
                        {
                            Console.WriteLine($"   🔗 {link.Url}");
                        }
                        if (foundLinks.Count > 3)
                        {
                            Console.WriteLine($"   ... und {foundLinks.Count - 3} weitere");
                        }
                    }
                    else
                    {
                        episode.UpdateStatus(EpisodeStatus.NoLinksFound);
                        consecutiveErrors++;
                        Console.WriteLine($"❌ Keine Links gefunden ({consecutiveErrors}/{options.MaxConsecutiveErrors})");
                    }

                    await _dbContext.SaveChangesAsync();
                    result.ProcessedEpisodes++;
                    totalProcessed++;

                    // Fortschritt anzeigen
                    if (totalProcessed % 5 == 0)
                    {
                        Console.WriteLine($"\n📊 ZWISCHENSTATUS nach {totalProcessed} Episoden:");
                        Console.WriteLine($"   🔗 Links gesamt: {result.TotalLinksFound}");
                        Console.WriteLine($"   ❌ Fehler: {result.TotalErrors}");
                        Console.WriteLine($"   ⏱️ Laufzeit: {result.Duration:mm\\:ss}");
                    }

                    // Nächste Episode-URL finden
                    var nextUrl = await GetNextEpisodeUrlAsync(extractor, currentUrl, currentSeason, currentEpisode);
                    if (nextUrl == null)
                    {
                        Console.WriteLine($"\n🔍 Keine weitere Episode gefunden - Serie abgeschlossen");
                        break;
                    }

                    // Prüfe ob wir in eine neue Staffel wechseln
                    var nextSeason = GetSeasonFromUrl(nextUrl);
                    if (nextSeason > currentSeason)
                    {
                        Console.WriteLine($"\n🎬 NEUE STAFFEL ERKANNT: S{currentSeason} → S{nextSeason}");

                        // Optional: Benutzer fragen ob weiter machen (falls interaktiv)
                        // Für jetzt automatisch weitermachen
                        Console.WriteLine($"🚀 Setze automatisch mit Staffel {nextSeason} fort");
                    }

                    currentUrl = nextUrl;
                    currentEpisode++;

                    // Kurze Pause zwischen Episoden
                    await Task.Delay(options.DelayBetweenEpisodes);
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    result.AddError($"S{currentSeason}E{currentEpisode}: {ex.Message}");

                    Console.WriteLine($"❌ FEHLER bei S{currentSeason:D2}E{currentEpisode:D2}: {ex.Message}");
                    Console.WriteLine($"   🔄 Fehleranzahl: {consecutiveErrors}/{options.MaxConsecutiveErrors}");

                    if (options.ContinueOnError)
                    {
                        // Versuche mit nächster Episode fortzufahren
                        var nextUrl = GenerateNextEpisodeUrl(currentUrl, currentSeason, currentEpisode + 1);
                        currentUrl = nextUrl;
                        currentEpisode++;

                        Console.WriteLine($"   ⏭️ Setze mit nächster Episode fort: {nextUrl}");
                        await Task.Delay(options.DelayAfterError);
                    }
                    else
                    {
                        throw; // Beende bei erstem Fehler
                    }
                }
            }

            // Finale Statistiken
            Console.WriteLine($"\n📊 FINALE STATISTIKEN:");
            Console.WriteLine($"   📈 Episoden verarbeitet: {result.ProcessedEpisodes}");
            Console.WriteLine($"   🔗 Links gefunden: {result.TotalLinksFound}");
            Console.WriteLine($"   ❌ Fehler: {result.TotalErrors}");

            if (result.HostStatistics.Any())
            {
                Console.WriteLine($"   🏠 Host-Verteilung:");
                foreach (var host in result.HostStatistics)
                {
                    Console.WriteLine($"      {host.Key}: {host.Value} Links");
                }
            }

            if (consecutiveErrors >= options.MaxConsecutiveErrors)
            {
                Console.WriteLine($"⚠️ Gestoppt nach {options.MaxConsecutiveErrors} aufeinanderfolgenden Fehlern");
            }
            else if (options.MaxEpisodes.HasValue && totalProcessed >= options.MaxEpisodes.Value)
            {
                Console.WriteLine($"✅ Episode-Limit von {options.MaxEpisodes} erreicht");
            }
            else
            {
                Console.WriteLine($"✅ Serie vollständig durchsucht");
            }
        }

        /// <summary>
        /// Finde die nächste Episode-URL (delegiert an Extraktor falls möglich)
        /// </summary>
        private async Task<string?> GetNextEpisodeUrlAsync(IHostExtractor extractor, string currentUrl,
            int currentSeason, int currentEpisode)
        {
            try
            {
                // 1. Versuche über Extraktor (falls VidmolyExtractor)
                if (extractor is Extractors.VidmolyExtractor vidmolyExtractor)
                {
                    var nextUrl = await vidmolyExtractor.GetNextEpisodeUrlAsync(currentUrl);
                    if (!string.IsNullOrEmpty(nextUrl) && nextUrl != currentUrl)
                    {
                        return nextUrl;
                    }
                }

                // 2. Fallback: URL-basierte Generation
                return GenerateNextEpisodeUrl(currentUrl, currentSeason, currentEpisode + 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Finden der nächsten Episode: {ex.Message}");
                return GenerateNextEpisodeUrl(currentUrl, currentSeason, currentEpisode + 1);
            }
        }

        /// <summary>
        /// Prüfe ob Episode bereits existiert
        /// </summary>
        private async Task<Episode?> GetExistingEpisodeAsync(Series series, int seasonNumber, int episodeNumber)
        {
            return await _dbContext.Episodes
                .Include(e => e.Links)
                .Include(e => e.Season)
                .FirstOrDefaultAsync(e =>
                    e.Season.SeriesId == series.Id &&
                    e.Season.Number == seasonNumber &&
                    e.Number == episodeNumber);
        }

        private async Task<Series> GetOrCreateSeriesAsync(string url, string? providedName)
        {
            var seriesName = providedName ?? ExtractSeriesNameFromUrl(url);
            var cleanName = CleanFileName(seriesName);

            var existingSeries = await _dbContext.GetSeriesByNameAsync(seriesName);
            if (existingSeries != null)
            {
                Console.WriteLine($"📺 Verwende existierende Serie: {seriesName}");
                return existingSeries;
            }

            var newSeries = new Series
            {
                Name = seriesName,
                CleanName = cleanName,
                OriginalUrl = url,
                Status = MediaStatus.Processing
            };

            _dbContext.Series.Add(newSeries);
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"📺 Neue Serie erstellt: {seriesName}");
            return newSeries;
        }

        private IHostExtractor? FindBestExtractor(string url, string preferredHost)
        {
            // Versuche zuerst den bevorzugten Extraktor
            if (preferredHost != "auto" && _extractors.ContainsKey(preferredHost.ToLower()))
            {
                var preferred = _extractors[preferredHost.ToLower()];
                if (preferred.CanHandle(url))
                {
                    return preferred;
                }
            }

            // Finde automatisch den besten Extraktor
            return _allExtractors.FirstOrDefault(e => e.CanHandle(url));
        }

        private async Task<Season> GetOrCreateSeasonAsync(Series series, int seasonNumber)
        {
            var season = series.Seasons.FirstOrDefault(s => s.Number == seasonNumber);
            if (season == null)
            {
                season = new Season
                {
                    Number = seasonNumber,
                    SeriesId = series.Id,
                    Series = series
                };
                series.Seasons.Add(season);
                _dbContext.Seasons.Add(season);
                await _dbContext.SaveChangesAsync();
                Console.WriteLine($"📁 Neue Staffel erstellt: S{seasonNumber}");
            }
            return season;
        }

        private async Task<Episode> GetOrCreateEpisodeAsync(Season season, int episodeNumber, string url)
        {
            var episode = season.Episodes.FirstOrDefault(e => e.Number == episodeNumber);
            if (episode == null)
            {
                episode = new Episode
                {
                    Number = episodeNumber,
                    SeasonId = season.Id,
                    Season = season,
                    OriginalUrl = url,
                    Title = ExtractEpisodeTitleFromUrl(url)
                };
                season.Episodes.Add(episode);
                _dbContext.Episodes.Add(episode);
                await _dbContext.SaveChangesAsync();
            }
            return episode;
        }

        // URL Parsing Utilities
        private string ExtractSeriesNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments;

                // Versuche verschiedene URL-Strukturen
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].Contains("stream") && i + 1 < segments.Length)
                    {
                        return CleanSeriesName(segments[i + 1]);
                    }
                    if (segments[i].Contains("serie") && i + 1 < segments.Length)
                    {
                        return CleanSeriesName(segments[i + 1]);
                    }
                }

                // Fallback: Verwende Domain-Name
                return uri.Host.Replace("www.", "").Replace(".com", "").Replace(".to", "");
            }
            catch
            {
                return "Unknown Series";
            }
        }

        private string CleanSeriesName(string rawName)
        {
            return rawName.TrimEnd('/')
                         .Replace("-", " ")
                         .Replace("_", " ")
                         .Trim();
        }

        private int GetSeasonFromUrl(string url)
        {
            var patterns = new[]
            {
                @"/staffel-(\d+)/",
                @"/season-(\d+)/",
                @"/s(\d+)/",
                @"season=(\d+)",
                @"staffel=(\d+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int season))
                {
                    return season;
                }
            }

            return 1; // Default Season
        }

        private int GetEpisodeFromUrl(string url)
        {
            var patterns = new[]
            {
                @"/episode-(\d+)",
                @"/ep-(\d+)",
                @"/e(\d+)",
                @"episode=(\d+)",
                @"ep=(\d+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int episode))
                {
                    return episode;
                }
            }

            return 1; // Default Episode
        }

        private string ExtractEpisodeTitleFromUrl(string url)
        {
            // Kann später erweitert werden um Titel aus der Seite zu extrahieren
            return "";
        }

        private string GenerateNextEpisodeUrl(string currentUrl, int season, int episode)
        {
            // Versuche Episode-Nummer in URL zu ersetzen
            var episodePatterns = new[]
            {
                (@"episode-(\d+)", $"episode-{episode}"),
                (@"ep-(\d+)", $"ep-{episode}"),
                (@"episode=(\d+)", $"episode={episode}"),
                (@"ep=(\d+)", $"ep={episode}")
            };

            foreach (var (pattern, replacement) in episodePatterns)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                if (regex.IsMatch(currentUrl))
                {
                    return regex.Replace(currentUrl, replacement);
                }
            }

            return currentUrl; // Fallback
        }

        private string CleanFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        public async Task CleanupAsync()
        {
            foreach (var extractor in _allExtractors)
            {
                await extractor.CleanupAsync();
            }
        }
    }
}