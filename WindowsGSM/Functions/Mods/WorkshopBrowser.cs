using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Mods
{
    public sealed class WorkshopBrowserItem
    {
        public string Id;
        public string Title;
        public string PreviewUrl;
    }

    /// <summary>
    /// Browses the public Steam Workshop catalogue for a game (no API key needed): it fetches the
    /// community "browse" page and scrapes the item id / title / preview image. Best-effort — Steam can
    /// change its markup; returns (false, error) rather than throwing.
    /// </summary>
    public static class WorkshopBrowser
    {
        private static readonly HttpClient _http = MakeClient();

        // The title lives in the <img alt="…"> next to the filedetails link, the id in the link.
        private static readonly Regex _rx = new Regex(
            "filedetails/\\?id=(\\d+)\"[^>]*>\\s*<img src=\"([^\"]+)\"\\s+alt=\"([^\"]*)\"",
            RegexOptions.Compiled);

        private static HttpClient MakeClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // A browser-like UA: Steam serves the same HTML, but avoids any bot filtering.
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            return c;
        }

        public static async Task<(bool ok, List<WorkshopBrowserItem> items, string err)> SearchAsync(string appId, string query, int page = 1)
        {
            if (string.IsNullOrWhiteSpace(appId)) { return (false, null, "no workshop app id"); }
            try
            {
                bool hasQuery = !string.IsNullOrWhiteSpace(query);
                string url = "https://steamcommunity.com/workshop/browse/?appid=" + Uri.EscapeDataString(appId)
                    + "&browsesort=" + (hasQuery ? "textsearch" : "trend")
                    + "&section=readytouseitems&actualsort=trend&p=" + Math.Max(1, page)
                    + "&searchtext=" + Uri.EscapeDataString(query ?? string.Empty);

                string html = await _http.GetStringAsync(url).ConfigureAwait(false);

                var list = new List<WorkshopBrowserItem>();
                var seen = new HashSet<string>();
                foreach (Match m in _rx.Matches(html))
                {
                    string id = m.Groups[1].Value;
                    if (!seen.Add(id)) { continue; }
                    list.Add(new WorkshopBrowserItem
                    {
                        Id = id,
                        PreviewUrl = m.Groups[2].Value,
                        Title = System.Net.WebUtility.HtmlDecode(m.Groups[3].Value)
                    });
                }
                return (true, list, null);
            }
            catch (Exception e)
            {
                return (false, null, e.Message);
            }
        }
    }
}
