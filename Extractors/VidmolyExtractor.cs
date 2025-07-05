using OpenQA.Selenium;
using System.Text.RegularExpressions;
using YtDlpExtractor.Core.Models;
using YtDlpExtractor.Extractors.Base;

namespace YtDlpExtractor.Extractors
{
    public class VidmolyExtractor : BaseExtractor
    {
        public override string HostName => "Vidmoly";

        private static readonly Regex VidmolyEmbedRegex = new Regex(
            @"https?://vidmoly\.to/embed-([a-zA-Z0-9]+)\.html",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override bool CanHandle(string url)
        {
            return url.Contains("aniworld.to") ||
                   url.Contains("vidmoly.to") ||
                   url.Contains("stream") ||
                   url.Contains("episode");
        }

        public override async Task<List<DownloadableLink>> ExtractLinksAsync(string episodeUrl, Episode episode)
        {
            var foundLinks = new List<DownloadableLink>();

            if (Driver == null)
            {
                Console.WriteLine("❌ WebDriver nicht initialisiert");
                return foundLinks;
            }

            try
            {
                Console.WriteLine($"🔍 Scraping Episode S{episode.Season.Number}E{episode.Number}: {episodeUrl}");

                // 1. Zur Episode-Seite navigieren
                Driver.Navigate().GoToUrl(episodeUrl);
                await WaitForPageLoad();
                Console.WriteLine($"📄 Seite geladen: {Driver.Title}");

                // 2. DIREKT nach dem Vidmoly-Link suchen basierend auf Screenshot-Struktur
                var vidmolyRedirectUrl = await FindVidmolyLinkDirectlyAsync();

                if (!string.IsNullOrEmpty(vidmolyRedirectUrl))
                {
                    Console.WriteLine($"🎯 Vidmoly-Link gefunden: {vidmolyRedirectUrl}");

                    // 3. Dem Link folgen
                    var vidmolyUrl = await FollowRedirectToVidmolyAsync(vidmolyRedirectUrl);
                    if (!string.IsNullOrEmpty(vidmolyUrl))
                    {
                        var downloadableLink = CreateDownloadableLink(vidmolyUrl, episode, LinkType.VidmolyEmbed);
                        foundLinks.Add(downloadableLink);
                        Console.WriteLine($"✅ Vidmoly-URL extrahiert: {vidmolyUrl}");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Kein Vidmoly-Link auf dieser Seite gefunden");
                }

                Console.WriteLine($"✅ {foundLinks.Count} Vidmoly-Link für Episode {episode.Number}");
                return foundLinks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Scraping: {ex.Message}");
                return foundLinks;
            }
        }

        /// <summary>
        /// Suche DIREKT nach dem Vidmoly-Link basierend auf der exakten HTML-Struktur
        /// </summary>
        private async Task<string?> FindVidmolyLinkDirectlyAsync()
        {
            try
            {
                // EXAKTE Suche nach: <a class="watchEpisode"> die ein <i class="icon Vidmoly"> enthalten
                // Verwende XPath für präzise Suche
                var xpath = "//a[@class='watchEpisode'][.//i[contains(@class,'icon') and contains(@class,'Vidmoly')]]";

                var vidmolyElements = Driver!.FindElements(By.XPath(xpath));
                Console.WriteLine($"   🎯 Gefundene Vidmoly-Links: {vidmolyElements.Count}");

                if (vidmolyElements.Count > 0)
                {
                    var href = vidmolyElements[0].GetAttribute("href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        // Vollständige URL erstellen
                        var fullUrl = href.StartsWith("/")
                            ? $"https://aniworld.to{href}"
                            : href;
                        Console.WriteLine($"   📎 Vidmoly-Redirect: {fullUrl}");
                        return fullUrl;
                    }
                }

                // Fallback: CSS-Selektor-Ansatz
                var watchEpisodeElements = Driver.FindElements(By.CssSelector("a.watchEpisode"));
                Console.WriteLine($"   🔍 Fallback: {watchEpisodeElements.Count} watchEpisode-Elemente");

                foreach (var element in watchEpisodeElements)
                {
                    try
                    {
                        // Prüfe ob ein Vidmoly-Icon drin ist
                        var vidmolyIcon = element.FindElements(By.CssSelector("i.icon.Vidmoly"));
                        if (vidmolyIcon.Count > 0)
                        {
                            var href = element.GetAttribute("href");
                            if (!string.IsNullOrEmpty(href))
                            {
                                var fullUrl = href.StartsWith("/")
                                    ? $"https://aniworld.to{href}"
                                    : href;
                                Console.WriteLine($"   📎 Fallback Vidmoly-Link: {fullUrl}");
                                return fullUrl;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠️ Element-Fehler: {ex.Message}");
                    }
                }

                Console.WriteLine($"   ❌ Kein Vidmoly-Link gefunden");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler bei direkter Vidmoly-Suche: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Verfolge einen Redirect-Link zu Vidmoly (SCHNELL)
        /// </summary>
        private async Task<string?> FollowRedirectToVidmolyAsync(string redirectUrl)
        {
            var originalUrl = Driver!.Url;

            try
            {
                Console.WriteLine($"   🔗 Folge Redirect: {redirectUrl}");

                // Navigiere zum Redirect-Link
                Driver.Navigate().GoToUrl(redirectUrl);

                // Warte NUR 2 Sekunden (nicht 4!)
                await Task.Delay(2000);

                var finalUrl = Driver.Url;
                Console.WriteLine($"   📄 Erreicht: {finalUrl}");

                // Prüfe ob wir eine Vidmoly-URL erreicht haben
                if (VidmolyEmbedRegex.IsMatch(finalUrl))
                {
                    Console.WriteLine($"   ✅ Vidmoly-URL erreicht!");
                    return finalUrl;
                }

                // SCHNELLE Suche nach Vidmoly-URLs auf der Seite
                var vidmolyUrls = await FindDirectVidmolyUrlsQuickAsync();
                if (vidmolyUrls.Count > 0)
                {
                    Console.WriteLine($"   ✅ Vidmoly-URL auf Seite gefunden!");
                    return vidmolyUrls[0];
                }

                Console.WriteLine($"   ❌ Keine Vidmoly-URL - andere Hoster");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Redirect-Fehler: {ex.Message}");
                return null;
            }
            finally
            {
                // Schnell zurück zur ursprünglichen Seite
                try
                {
                    Driver.Navigate().GoToUrl(originalUrl);
                    await Task.Delay(500); // Nur kurz warten
                }
                catch { }
            }
        }

        /// <summary>
        /// SCHNELLE Suche nach Vidmoly-URLs (ohne Player-Aktivierung)
        /// </summary>
        private async Task<List<string>> FindDirectVidmolyUrlsQuickAsync()
        {
            var foundUrls = new List<string>();

            try
            {
                // 1. Aktuelle URL prüfen
                if (VidmolyEmbedRegex.IsMatch(Driver!.Url))
                {
                    foundUrls.Add(Driver.Url);
                    return foundUrls;
                }

                // 2. iframes
                var iframes = Driver.FindElements(By.CssSelector("iframe[src*='vidmoly']"));
                foreach (var iframe in iframes)
                {
                    var src = iframe.GetAttribute("src");
                    if (!string.IsNullOrEmpty(src) && VidmolyEmbedRegex.IsMatch(src))
                    {
                        foundUrls.Add(src);
                        return foundUrls; // Ersten gefunden = fertig
                    }
                }

                // 3. Links
                var links = Driver.FindElements(By.CssSelector("a[href*='vidmoly.to/embed-']"));
                foreach (var link in links)
                {
                    var href = link.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href) && VidmolyEmbedRegex.IsMatch(href))
                    {
                        foundUrls.Add(href);
                        return foundUrls; // Ersten gefunden = fertig
                    }
                }

                // 4. Page Source (nur wenn nichts anderes gefunden)
                var pageSource = Driver.PageSource;
                var match = VidmolyEmbedRegex.Match(pageSource);
                if (match.Success)
                {
                    foundUrls.Add(match.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Quick-Search Fehler: {ex.Message}");
            }

            return foundUrls;
        }

        public override async Task<bool> ValidateLinkAsync(string url)
        {
            try
            {
                if (!VidmolyEmbedRegex.IsMatch(url))
                    return false;

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5); // Auch hier schneller

                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private DownloadableLink CreateDownloadableLink(string url, Episode episode, LinkType type)
        {
            return new DownloadableLink
            {
                EpisodeId = episode.Id,
                Episode = episode,
                Url = url,
                HostName = HostName,
                Type = type,
                Quality = LinkQuality.Unknown,
                IsValid = true,
                IsTested = false,
                FoundAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Ermittle die nächste Episode-URL
        /// </summary>
        public async Task<string?> GetNextEpisodeUrlAsync(string currentUrl)
        {
            try
            {
                // URL-basierte Generierung (schnell)
                var episodeMatch = Regex.Match(currentUrl, @"episode-(\d+)");
                if (episodeMatch.Success)
                {
                    var currentEpisode = int.Parse(episodeMatch.Groups[1].Value);
                    var nextEpisode = currentEpisode + 1;
                    return currentUrl.Replace($"episode-{currentEpisode}", $"episode-{nextEpisode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Next-Episode Fehler: {ex.Message}");
                return null;
            }
        }
    }
}