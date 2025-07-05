using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using YtDlpExtractor.Core.Models;
using System.Text.RegularExpressions;
using YtDlpExtractor.Core.Models;

namespace YtDlpExtractor.Core.Interfaces
{
    // Interface für alle Host-Extraktoren
    public interface IHostExtractor
    {
        string HostName { get; }
        Task<List<DownloadableLink>> ExtractLinksAsync(string episodeUrl, Episode episode);
        Task<bool> ValidateLinkAsync(string url);
        bool CanHandle(string url);
        Task<bool> InitializeAsync();
        Task CleanupAsync();
    }
}