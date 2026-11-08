using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class SearchItem
    {
        public string LcscId { get; set; } = "";
        public string Mpn { get; set; } = "";
        public string Package { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public int Stock { get; set; }
        public double Price { get; set; }
        public bool Rohs { get; set; }

        public string StockText => Stock > 0 ? Stock.ToString("N0") : "-";
        public string PriceText => Price > 0 ? "¥" + Price.ToString("0.0000") : "-";
    }

    internal sealed class SearchPage
    {
        public int Total;
        public readonly List<SearchItem> Items = new List<SearchItem>();
    }

    internal static class SearchClient
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(EeEndpoints.Current.RequestTimeoutSeconds),
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            return client;
        }

        public static async Task<SearchPage> SearchAsync(
            string keyword, int page, int pageSize, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new SearchPage();

            var config = EeEndpoints.Current;
            Exception last = null;
            foreach (var host in config.SearchHosts)
            {
                var path = config.SearchPath;
                var url = string.Format("{0}{1}{2}keyword={3}&currentPage={4}&pageSize={5}&version={6}",
                    host, path, path.IndexOf('?') >= 0 ? "&" : "?",
                    Uri.EscapeDataString(keyword.Trim()), page, pageSize, config.ApiVersion);
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("Referer", host + "/");
                        using (var response = await Client.SendAsync(request, cancellationToken)
                                   .ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                last = new InvalidOperationException(
                                    "搜索接口返回 HTTP " + (int)response.StatusCode);
                                continue;
                            }
                            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return Parse(text);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            throw new InvalidOperationException("无法访问立创搜索接口，请检查网络。", last);
        }

        private static SearchPage Parse(string json)
        {
            var result = new SearchPage();
            var root = JObject.Parse(json);
            if ((int?)root["code"] != 200) return result;

            var payload = JsonUtil.Obj(root["result"]);
            result.Total = (int?)payload["total"] ?? 0;

            foreach (var token in JsonUtil.Arr(payload["productList"]))
            {
                var item = new SearchItem
                {
                    LcscId = JsonUtil.Str(token["number"]),
                    Mpn = JsonUtil.Str(token["mpn"]),
                    Package = JsonUtil.Str(token["package"]),
                    Manufacturer = JsonUtil.Str(token["manufacturer"]),
                    Stock = (int?)token["stock"] ?? 0,
                    Rohs = (bool?)token["ifRoHS"] ?? false,
                };

                var images = token["image"] as JArray;
                if (images != null && images.Count > 0)
                {
                    item.ImageUrl = JsonUtil.Str(images[0]["96x96"]);
                }

                var prices = token["price"] as JArray;
                if (prices != null && prices.Count > 0)
                {
                    var tier = prices[0] as JArray;
                    if (tier != null && tier.Count > 1)
                    {
                        if (double.TryParse(JsonUtil.Str(tier[1]), out var price))
                            item.Price = price;
                    }
                }

                if (!string.IsNullOrWhiteSpace(item.LcscId))
                    result.Items.Add(item);
            }

            return result;
        }
    }
}
