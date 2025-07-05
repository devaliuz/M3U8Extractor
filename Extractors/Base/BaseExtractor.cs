using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using YtDlpExtractor.Core.Interfaces;
using YtDlpExtractor.Core.Models;

namespace YtDlpExtractor.Extractors.Base
{
    public abstract class BaseExtractor : IHostExtractor
    {
        protected IWebDriver? Driver { get; private set; }
        protected WebDriverWait? Wait { get; private set; }

        public abstract string HostName { get; }

        public virtual async Task<bool> InitializeAsync()
        {
            try
            {
                Driver = CreateWebDriver();
                Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(10));

                // Setup Network Monitoring für alle Extraktoren
                SetupNetworkMonitoring();

                Console.WriteLine($"🔧 {HostName} Extraktor initialisiert");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Initialisieren von {HostName}: {ex.Message}");
                return false;
            }
        }

        public virtual async Task CleanupAsync()
        {
            try
            {
                Driver?.Quit();
                Driver?.Dispose();
                Console.WriteLine($"🧹 {HostName} Extraktor bereinigt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Cleanup-Fehler {HostName}: {ex.Message}");
            }
        }

        protected virtual IWebDriver CreateWebDriver()
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

            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            return new ChromeDriver(service, options);
        }

        protected virtual void SetupNetworkMonitoring()
        {
            if (Driver == null) return;

            var script = @"
                window.foundUrls = window.foundUrls || [];
                
                // Monitor fetch requests
                if (window.fetch) {
                    const originalFetch = window.fetch;
                    window.fetch = function(...args) {
                        const url = args[0];
                        if (typeof url === 'string') {
                            window.foundUrls.push(url);
                        }
                        return originalFetch.apply(this, args);
                    };
                }
                
                // Monitor XMLHttpRequest
                const originalOpen = XMLHttpRequest.prototype.open;
                XMLHttpRequest.prototype.open = function(method, url, ...args) {
                    if (typeof url === 'string') {
                        window.foundUrls.push(url);
                    }
                    return originalOpen.apply(this, [method, url, ...args]);
                };
            ";

            ((IJavaScriptExecutor)Driver).ExecuteScript(script);
        }

        protected async Task<bool> WaitForPageLoad()
        {
            if (Driver == null || Wait == null) return false;

            try
            {
                Wait.Until(driver => ((IJavaScriptExecutor)driver)
                    .ExecuteScript("return document.readyState").Equals("complete"));
                await Task.Delay(500);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Page Load Timeout: {ex.Message}");
                return false;
            }
        }

        public abstract Task<List<DownloadableLink>> ExtractLinksAsync(string episodeUrl, Episode episode);
        public abstract Task<bool> ValidateLinkAsync(string url);
        public abstract bool CanHandle(string url);
    }
}