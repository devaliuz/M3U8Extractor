using System.Text.RegularExpressions;

namespace YtDlpExtractor.Configuration
{
    /// <summary>
    /// Host-spezifische Einstellungen für alle Extraktoren
    /// </summary>
    public class HostSettings
    {
        public VidmolySettings Vidmoly { get; set; } = new VidmolySettings();
        public GenericHostSettings Generic { get; set; } = new GenericHostSettings();

        // Weitere Hoster können hier hinzugefügt werden
        // public StreamtapeSettings Streamtape { get; set; } = new StreamtapeSettings();
        // public VoeSettings Voe { get; set; } = new VoeSettings();

        public void Validate()
        {
            Vidmoly.Validate();
            Generic.Validate();
        }

        public IHostExtractorSettings GetSettingsForHost(string hostName)
        {
            return hostName.ToLower() switch
            {
                "vidmoly" => Vidmoly,
                _ => Generic
            };
        }
    }

    /// <summary>
    /// Interface für alle Host-Extractor-Settings
    /// </summary>
    public interface IHostExtractorSettings
    {
        string HostName { get; }
        bool IsEnabled { get; set; }
        int TimeoutSeconds { get; set; }
        int MaxRetries { get; set; }
        int DelayBetweenRequests { get; set; }
        void Validate();
    }

    /// <summary>
    /// Vidmoly-spezifische Einstellungen
    /// </summary>
    public class VidmolySettings : IHostExtractorSettings
    {
        public string HostName => "Vidmoly";
        public bool IsEnabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 3;
        public int DelayBetweenRequests { get; set; } = 500; // Milliseconds
        public int PageLoadTimeoutSeconds { get; set; } = 10;
        public int RedirectFollowTimeout { get; set; } = 15; // Sekunden
        public bool EnableNetworkMonitoring { get; set; } = true;
        public bool SavePageSource { get; set; } = false;

        // Vidmoly-spezifische URL-Patterns
        public List<string> EmbedUrlPatterns { get; set; } = new List<string>
        {
            @"https?://vidmoly\.to/embed-([a-zA-Z0-9]+)\.html",
            @"https?://vidmoly\.com/embed-([a-zA-Z0-9]+)\.html"
        };

        // CSS-Selektoren für Vidmoly
        public VidmolySelectors Selectors { get; set; } = new VidmolySelectors();

        // Validierungs-Einstellungen
        public ValidationSettings Validation { get; set; } = new ValidationSettings();

        public void Validate()
        {
            if (TimeoutSeconds <= 0)
                throw new ArgumentException("TimeoutSeconds muss größer als 0 sein");

            if (MaxRetries < 0)
                throw new ArgumentException("MaxRetries darf nicht negativ sein");

            if (DelayBetweenRequests < 0)
                throw new ArgumentException("DelayBetweenRequests darf nicht negativ sein");

            if (PageLoadTimeoutSeconds <= 0)
                throw new ArgumentException("PageLoadTimeoutSeconds muss größer als 0 sein");

            if (RedirectFollowTimeout <= 0)
                throw new ArgumentException("RedirectFollowTimeout muss größer als 0 sein");

            if (EmbedUrlPatterns.Count == 0)
                throw new ArgumentException("Mindestens ein EmbedUrlPattern muss definiert sein");

            // Validiere Regex-Patterns
            foreach (var pattern in EmbedUrlPatterns)
            {
                try
                {
                    _ = new Regex(pattern);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Ungültiger Regex-Pattern '{pattern}': {ex.Message}");
                }
            }

            Selectors.Validate();
            Validation.Validate();
        }

        public bool IsVidmolyUrl(string url)
        {
            return EmbedUrlPatterns.Any(pattern => Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase));
        }

        public Regex[] GetCompiledPatterns()
        {
            return EmbedUrlPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToArray();
        }
    }

    /// <summary>
    /// CSS-Selektoren für Vidmoly
    /// </summary>
    public class VidmolySelectors
    {
        public List<string> StreamLinkSelectors { get; set; } = new List<string>
        {
            ".watchEpisode",
            ".hosterSiteVideoButton",
            ".generateInlinePlayer a",
            "a[href*='redirect']",
            "li[data-link-target] a",
            "a[data-episode-id]",
            ".hostingSiteVideoButton"
        };

        public List<string> IframeSelectors { get; set; } = new List<string>
        {
            "iframe[src*='vidmoly']",
            "iframe[data-src*='vidmoly']"
        };

        public List<string> DirectLinkSelectors { get; set; } = new List<string>
        {
            "a[href*='vidmoly.to/embed-']",
            "a[data-url*='vidmoly']"
        };

        public List<string> HiddenInputSelectors { get; set; } = new List<string>
        {
            "input[type='hidden'][value*='vidmoly']"
        };

        public List<string> PlayButtonSelectors { get; set; } = new List<string>
        {
            ".vjs-big-play-button",
            ".jw-display-icon-container",
            "button[aria-label*='play']",
            ".play-button",
            "video"
        };

        public void Validate()
        {
            if (StreamLinkSelectors.Count == 0)
                throw new ArgumentException("Mindestens ein StreamLinkSelector muss definiert sein");
        }
    }

    /// <summary>
    /// Validierungs-Einstellungen für Vidmoly
    /// </summary>
    public class ValidationSettings
    {
        public bool ValidateLinksBeforeStorage { get; set; } = true;
        public bool UseHeadRequestForValidation { get; set; } = true;
        public int ValidationTimeoutSeconds { get; set; } = 10;
        public bool AcceptRedirects { get; set; } = true;
        public int MaxRedirects { get; set; } = 5;
        public List<int> AcceptedStatusCodes { get; set; } = new List<int> { 200, 302, 301 };

        public void Validate()
        {
            if (ValidationTimeoutSeconds <= 0)
                throw new ArgumentException("ValidationTimeoutSeconds muss größer als 0 sein");

            if (MaxRedirects < 0)
                throw new ArgumentException("MaxRedirects darf nicht negativ sein");

            if (AcceptedStatusCodes.Count == 0)
                throw new ArgumentException("Mindestens ein AcceptedStatusCode muss definiert sein");
        }
    }

    /// <summary>
    /// Generische Host-Einstellungen für unbekannte Hoster
    /// </summary>
    public class GenericHostSettings : IHostExtractorSettings
    {
        public string HostName => "Generic";
        public bool IsEnabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 20;
        public int MaxRetries { get; set; } = 2;
        public int DelayBetweenRequests { get; set; } = 1000; // Milliseconds
        public int PageLoadTimeoutSeconds { get; set; } = 8;
        public bool EnableFallbackExtraction { get; set; } = true;

        // Generische Selektoren für M3U8/Video-Links
        public List<string> VideoLinkSelectors { get; set; } = new List<string>
        {
            "video[src]",
            "source[src]",
            "a[href*='.m3u8']",
            "a[href*='.mp4']",
            ".video-link",
            ".stream-link"
        };

        public List<string> IframeLinkSelectors { get; set; } = new List<string>
        {
            "iframe[src]",
            "embed[src]"
        };

        // Häufige URL-Patterns für Streaming-Sites
        public List<string> CommonStreamPatterns { get; set; } = new List<string>
        {
            @"https?://[^/]+/[^/]*\.m3u8",
            @"https?://[^/]+/[^/]*\.mp4",
            @"https?://[^/]+/embed/[^/]+",
            @"https?://[^/]+/player/[^/]+"
        };

        public void Validate()
        {
            if (TimeoutSeconds <= 0)
                throw new ArgumentException("TimeoutSeconds muss größer als 0 sein");

            if (MaxRetries < 0)
                throw new ArgumentException("MaxRetries darf nicht negativ sein");

            if (DelayBetweenRequests < 0)
                throw new ArgumentException("DelayBetweenRequests darf nicht negativ sein");

            if (PageLoadTimeoutSeconds <= 0)
                throw new ArgumentException("PageLoadTimeoutSeconds muss größer als 0 sein");
        }

        public bool MatchesPattern(string url)
        {
            return CommonStreamPatterns.Any(pattern => Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase));
        }
    }

    /// <summary>
    /// Factory für Host-Settings
    /// </summary>
    public static class HostSettingsFactory
    {
        private static HostSettings? _instance;
        private static readonly object _lock = new object();

        public static HostSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= LoadDefault();
                    }
                }
                return _instance;
            }
        }

        public static HostSettings LoadDefault()
        {
            var settings = new HostSettings();
            settings.Validate();
            return settings;
        }

        public static HostSettings LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return LoadDefault();

            try
            {
                var json = File.ReadAllText(filePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<HostSettings>(json);
                settings?.Validate();
                return settings ?? LoadDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Laden der Host-Settings aus {filePath}: {ex.Message}");
                return LoadDefault();
            }
        }

        public static void SaveToFile(HostSettings settings, string filePath)
        {
            try
            {
                settings.Validate();
                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Speichern der Host-Settings nach {filePath}: {ex.Message}");
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }
    }
}