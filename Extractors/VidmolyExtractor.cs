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

        // Pattern für Episode-URLs (anpassbar je nach Website)
        private static readonly Regex EpisodeUrlPattern = new Regex(
            @"/episode-(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SeasonUrlPattern = new Regex(
            @"/staffel-(\d+)/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override bool CanHandle(string url)
        {
            // Kann sowohl direkte Vidmoly-URLs als auch Streaming-Website-URLs handhaben
            return url.Contains("vidmoly.to") ||
                   url.Contains("stream") ||
                   url.Contains("episode") ||
                   url.Contains("staffel") ||
                   url.Contains("serie");
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

                // 1. Navigiere zur Episode-Seite
                Driver.Navigate().GoToUrl(episodeUrl);
                await WaitForPageLoad();

                Console.WriteLine($"📄 Seite geladen: {Driver.Title}");

                // 2. Versuche direkt Vidmoly-Embed-URLs zu finden (falls schon vorhanden)
                var directEmbedUrls = await FindDirectVidmolyEmbedsAsync();
                if (directEmbedUrls.Count > 0)
                {
                    Console.WriteLine($"🎯 {directEmbedUrls.Count} direkte Vidmoly-URLs gefunden");
                    foreach (var url in directEmbedUrls)
                    {
                        foundLinks.Add(CreateDownloadableLink(url, episode, LinkType.VidmolyEmbed));
                    }
                    return foundLinks;
                }

                // 3. Suche Stream-Links auf der Hauptseite
                var streamLinks = await FindStreamLinksAsync();
                Console.WriteLine($"🔗 {streamLinks.Count} Stream-Links gefunden");

                // 4. Folge jedem Stream-Link und suche nach Vidmoly-URLs
                foreach (var streamLink in streamLinks)
                {
                    var vidmolyUrls = await FollowStreamLinkToVidmolyAsync(streamLink);
                    foreach (var url in vidmolyUrls)
                    {
                        if (!foundLinks.Any(l => l.Url == url)) // Duplikate vermeiden
                        {
                            foundLinks.Add(CreateDownloadableLink(url, episode, LinkType.VidmolyEmbed));
                        }
                    }
                }

                Console.WriteLine($"✅ {foundLinks.Count} finale Vidmoly-URLs für Episode {episode.Number} gefunden");
                return foundLinks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Scraping von Episode {episode.Number}: {ex.Message}");
                return foundLinks;
            }
        }

        /// <summary>
        /// Suche direkt nach Vidmoly-Embed-URLs auf der aktuellen Seite
        /// </summary>
        private async Task<List<string>> FindDirectVidmolyEmbedsAsync()
        {
            var foundUrls = new List<string>();

            try
            {
                // 1. Suche in iframe src Attributen
                var iframes = Driver!.FindElements(By.TagName("iframe"));
                foreach (var iframe in iframes)
                {
                    var src = iframe.GetAttribute("src");
                    if (!string.IsNullOrEmpty(src) && VidmolyEmbedRegex.IsMatch(src))
                    {
                        foundUrls.Add(src);
                        Console.WriteLine($"   🎯 Iframe: {src}");
                    }
                }

                // 2. Suche in Link href Attributen
                var links = Driver.FindElements(By.CssSelector("a[href*='vidmoly']"));
                foreach (var link in links)
                {
                    var href = link.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href) && VidmolyEmbedRegex.IsMatch(href))
                    {
                        foundUrls.Add(href);
                        Console.WriteLine($"   🎯 Link: {href}");
                    }
                }

                // 3. Suche im Page Source (JavaScript-Variablen etc.)
                var pageSourceUrls = ExtractVidmolyUrlsFromPageSource();
                foundUrls.AddRange(pageSourceUrls);

                // 4. Suche in versteckten Input-Feldern
                var hiddenInputs = Driver.FindElements(By.CssSelector("input[type='hidden']"));
                foreach (var input in hiddenInputs)
                {
                    var value = input.GetAttribute("value");
                    if (!string.IsNullOrEmpty(value) && VidmolyEmbedRegex.IsMatch(value))
                    {
                        foundUrls.Add(value);
                        Console.WriteLine($"   🎯 Hidden Input: {value}");
                    }
                }

                // 5. Prüfe Network Monitoring
                var networkUrls = GetVidmolyUrlsFromNetworkMonitoring();
                foundUrls.AddRange(networkUrls);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Suchen direkter Embeds: {ex.Message}");
            }

            return foundUrls.Distinct().ToList();
        }

        /// <summary>
        /// Finde alle Stream-Links auf der Episode-Seite (die zu Vidmoly führen könnten)
        /// </summary>
        private async Task<List<string>> FindStreamLinksAsync()
        {
            var streamUrls = new List<string>();

            try
            {
                // Standard-Selektoren für Streaming-Websites
                var streamSelectors = new[]
                {
                    ".watchEpisode",
                    ".hosterSiteVideoButton",
                    ".generateInlinePlayer a",
                    ".hostingSiteVideoButton",
                    "a[href*='redirect']",
                    "li[data-link-target] a",
                    "a[data-episode-id]",
                    ".stream-link",
                    ".video-link",
                    ".hoster-link",
                    "a[href*='/redirect/']",
                    "a[href*='/stream/']",
                    "button[data-url]",
                    ".btn-stream"
                };

                foreach (var selector in streamSelectors)
                {
                    try
                    {
                        var elements = Driver!.FindElements(By.CssSelector(selector));
                        Console.WriteLine($"   🔍 Selector '{selector}': {elements.Count} Elemente");

                        foreach (var element in elements.Take(5)) // Limit pro Selector
                        {
                            // Versuche href Attribut
                            var href = element.GetAttribute("href");
                            if (!string.IsNullOrEmpty(href) && IsValidStreamLink(href))
                            {
                                if (!streamUrls.Contains(href))
                                {
                                    streamUrls.Add(href);
                                    Console.WriteLine($"   📎 Stream-Link: {href}");
                                }
                            }

                            // Versuche data-url Attribut
                            var dataUrl = element.GetAttribute("data-url");
                            if (!string.IsNullOrEmpty(dataUrl) && IsValidStreamLink(dataUrl))
                            {
                                if (!streamUrls.Contains(dataUrl))
                                {
                                    streamUrls.Add(dataUrl);
                                    Console.WriteLine($"   📎 Data-URL: {dataUrl}");
                                }
                            }

                            // Versuche onclick JavaScript
                            var onclick = element.GetAttribute("onclick");
                            if (!string.IsNullOrEmpty(onclick))
                            {
                                var extractedUrls = ExtractUrlsFromJavaScript(onclick);
                                foreach (var url in extractedUrls)
                                {
                                    if (!streamUrls.Contains(url))
                                    {
                                        streamUrls.Add(url);
                                        Console.WriteLine($"   📎 JS-Extracted: {url}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Fehler bei Selector {selector}: {ex.Message}");
                    }
                }

                // Zusätzlich: Suche alle Links, die verdächtig aussehen
                await FindAdditionalStreamLinksAsync(streamUrls);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Suchen von Stream-Links: {ex.Message}");
            }

            return streamUrls.Distinct().ToList();
        }

        /// <summary>
        /// Folge einem Stream-Link und extrahiere finale Vidmoly-URLs
        /// </summary>
        private async Task<List<string>> FollowStreamLinkToVidmolyAsync(string streamLink)
        {
            var foundVidmolyUrls = new List<string>();
            var originalUrl = Driver!.Url;

            try
            {
                Console.WriteLine($"🔗 Folge Stream-Link: {streamLink}");

                // Vollständige URL erstellen falls nötig
                var fullStreamUrl = streamLink.StartsWith("http")
                    ? streamLink
                    : new Uri(new Uri(originalUrl), streamLink).ToString();

                // Navigiere zum Stream-Link
                Driver.Navigate().GoToUrl(fullStreamUrl);
                await WaitForPageLoad();
                await Task.Delay(2000); // Warte auf Redirects/JavaScript

                Console.WriteLine($"   📄 Umgeleitet zu: {Driver.Url}");

                // 1. Prüfe ob wir direkt auf einer Vidmoly-Seite sind
                if (VidmolyEmbedRegex.IsMatch(Driver.Url))
                {
                    foundVidmolyUrls.Add(Driver.Url);
                    Console.WriteLine($"   ✅ Direkte Vidmoly-URL: {Driver.Url}");
                }

                // 2. Suche nach Vidmoly-URLs auf der aktuellen Seite
                var embedUrls = await FindDirectVidmolyEmbedsAsync();
                foundVidmolyUrls.AddRange(embedUrls);

                // 3. Versuche Play-Buttons zu klicken (falls vorhanden)
                await TryActivateVideoPlayerAsync();
                await Task.Delay(1500);

                // 4. Nochmals nach URLs suchen nach Player-Aktivierung
                var additionalUrls = await FindDirectVidmolyEmbedsAsync();
                foreach (var url in additionalUrls)
                {
                    if (!foundVidmolyUrls.Contains(url))
                    {
                        foundVidmolyUrls.Add(url);
                    }
                }

                // 5. Zurück zur ursprünglichen Seite
                Driver.Navigate().GoToUrl(originalUrl);
                await WaitForPageLoad();

                if (foundVidmolyUrls.Count > 0)
                {
                    Console.WriteLine($"   ✅ {foundVidmolyUrls.Count} Vidmoly-URL(s) extrahiert");
                }
                else
                {
                    Console.WriteLine($"   ❌ Keine Vidmoly-URLs gefunden");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Folgen von Stream-Link: {ex.Message}");

                // Versuche zurück zur ursprünglichen Seite
                try
                {
                    Driver.Navigate().GoToUrl(originalUrl);
                    await WaitForPageLoad();
                }
                catch { }
            }

            return foundVidmolyUrls.Distinct().ToList();
        }

        /// <summary>
        /// Versuche Video-Player zu aktivieren (Play-Buttons klicken)
        /// </summary>
        private async Task TryActivateVideoPlayerAsync()
        {
            var playButtonSelectors = new[]
            {
                ".vjs-big-play-button",
                ".jw-display-icon-container",
                "button[aria-label*='play']",
                ".play-button",
                ".btn-play",
                "video",
                ".video-play-button",
                "[class*='play']"
            };

            foreach (var selector in playButtonSelectors)
            {
                try
                {
                    var elements = Driver!.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0)
                    {
                        var element = elements[0];
                        if (element.Displayed && element.Enabled)
                        {
                            Console.WriteLine($"   🎮 Klicke Play-Button: {selector}");
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].click();", element);
                            await Task.Delay(1000);
                            break; // Nur einen Play-Button klicken
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Play-Button Fehler ({selector}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Prüfe ob ein Link ein valider Stream-Link ist
        /// </summary>
        private bool IsValidStreamLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            // Ignoriere JavaScript-Pseudo-URLs
            if (url.StartsWith("javascript:") || url == "#") return false;

            // Ignoriere externe Seiten
            if (url.Contains("facebook.com") || url.Contains("twitter.com")) return false;

            // Akzeptiere relative und absolute URLs
            return url.StartsWith("/") || url.StartsWith("http");
        }

        /// <summary>
        /// Extrahiere URLs aus JavaScript Code
        /// </summary>
        private List<string> ExtractUrlsFromJavaScript(string jsCode)
        {
            var urls = new List<string>();

            // Einfache URL-Extraktion aus JavaScript
            var urlPattern = new Regex(@"['""]([^'""]*(?:redirect|stream|embed)[^'""]*)['""]",
                RegexOptions.IgnoreCase);

            var matches = urlPattern.Matches(jsCode);
            foreach (Match match in matches)
            {
                var url = match.Groups[1].Value;
                if (IsValidStreamLink(url))
                {
                    urls.Add(url);
                }
            }

            return urls;
        }

        /// <summary>
        /// Zusätzliche Stream-Link-Suche
        /// </summary>
        private async Task FindAdditionalStreamLinksAsync(List<string> existingUrls)
        {
            try
            {
                // Suche alle Links mit verdächtigen Mustern
                var allLinks = Driver!.FindElements(By.TagName("a"));

                foreach (var link in allLinks)
                {
                    var href = link.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href) &&
                        !existingUrls.Contains(href) &&
                        (href.Contains("redirect") ||
                         href.Contains("stream") ||
                         href.Contains("embed") ||
                         href.Contains("watch")))
                    {
                        existingUrls.Add(href);
                        Console.WriteLine($"   📎 Zusätzlicher Link: {href}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler bei zusätzlicher Link-Suche: {ex.Message}");
            }
        }

        /// <summary>
        /// Extrahiere Vidmoly-URLs aus Page Source
        /// </summary>
        private List<string> ExtractVidmolyUrlsFromPageSource()
        {
            var foundUrls = new List<string>();

            try
            {
                var pageSource = Driver!.PageSource;
                var matches = VidmolyEmbedRegex.Matches(pageSource);

                foreach (Match match in matches)
                {
                    var url = match.Value;
                    if (!foundUrls.Contains(url))
                    {
                        foundUrls.Add(url);
                        Console.WriteLine($"   🎯 Page Source: {url}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Page Source Parsing: {ex.Message}");
            }

            return foundUrls;
        }

        /// <summary>
        /// Hole Vidmoly-URLs aus Network Monitoring
        /// </summary>
        private List<string> GetVidmolyUrlsFromNetworkMonitoring()
        {
            var foundUrls = new List<string>();

            try
            {
                var jsUrls = ((IJavaScriptExecutor)Driver!).ExecuteScript(@"
                    return (window.foundUrls || []).filter(url => 
                        url.includes('vidmoly.to/embed-') && url.includes('.html')
                    );
                ") as System.Collections.ObjectModel.ReadOnlyCollection<object>;

                if (jsUrls != null)
                {
                    foreach (var urlObj in jsUrls)
                    {
                        var url = urlObj?.ToString();
                        if (!string.IsNullOrEmpty(url) && !foundUrls.Contains(url))
                        {
                            foundUrls.Add(url);
                            Console.WriteLine($"   🎯 Network Monitor: {url}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Network Monitoring Fehler: {ex.Message}");
            }

            return foundUrls;
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
                Quality = LinkQuality.Unknown, // Wird später durch yt-dlp bestimmt
                IsValid = true,
                IsTested = false,
                FoundAt = DateTime.UtcNow
            };
        }

        public override async Task<bool> ValidateLinkAsync(string url)
        {
            try
            {
                // Prüfe URL-Format
                if (!VidmolyEmbedRegex.IsMatch(url))
                {
                    return false;
                }

                // HTTP HEAD Request zur Validierung
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Link-Validierung fehlgeschlagen für {url}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ermittle die nächste Episode-URL für automatische Navigation
        /// </summary>
        public async Task<string?> GetNextEpisodeUrlAsync(string currentUrl)
        {
            try
            {
                // Versuche zuerst über Episode-Navigation auf der Seite
                var nextUrl = await FindNextEpisodeFromPageAsync();
                if (!string.IsNullOrEmpty(nextUrl))
                {
                    return nextUrl;
                }

                // Fallback: URL-basierte Navigation
                return GenerateNextEpisodeUrl(currentUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Finden der nächsten Episode: {ex.Message}");
                return GenerateNextEpisodeUrl(currentUrl);
            }
        }

        /// <summary>
        /// Suche Next-Episode-Link auf der aktuellen Seite
        /// </summary>
        private async Task<string?> FindNextEpisodeFromPageAsync()
        {
            try
            {
                var nextSelectors = new[]
                {
                    "a[title*='nächste']",
                    "a[title*='next']",
                    ".next-episode",
                    ".episode-next",
                    "a[href*='episode-'][href*='+1']",
                    ".pagination .next"
                };

                foreach (var selector in nextSelectors)
                {
                    var elements = Driver!.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0)
                    {
                        var href = elements[0].GetAttribute("href");
                        if (!string.IsNullOrEmpty(href) && href.Contains("episode"))
                        {
                            Console.WriteLine($"🔗 Nächste Episode gefunden: {href}");
                            return href;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler bei Next-Episode-Suche: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Generiere nächste Episode-URL basierend auf URL-Pattern
        /// </summary>
        private string GenerateNextEpisodeUrl(string currentUrl)
        {
            var episodeMatch = EpisodeUrlPattern.Match(currentUrl);
            if (episodeMatch.Success)
            {
                var currentEpisode = int.Parse(episodeMatch.Groups[1].Value);
                var nextEpisode = currentEpisode + 1;

                var nextUrl = currentUrl.Replace($"episode-{currentEpisode}", $"episode-{nextEpisode}");
                Console.WriteLine($"🔗 Generierte nächste Episode-URL: {nextUrl}");
                return nextUrl;
            }

            return currentUrl; // Fallback
        }
    }
}