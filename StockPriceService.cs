using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SMT;

/// <summary>
/// Stock price data returned from API.
/// </summary>
public class StockPrice
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal YesterdayClose { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }

    public decimal Change => CurrentPrice - YesterdayClose;
    public decimal ChangePercent => YesterdayClose != 0
        ? Math.Round((CurrentPrice - YesterdayClose) / YesterdayClose * 100, 2)
        : 0;

    public bool IsUp => CurrentPrice >= YesterdayClose;
}

/// <summary>
/// Stock search suggestion item.
/// </summary>
public class StockSuggestion
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // e.g. "sh600519"
    public string Market { get; set; } = string.Empty; // "SH" or "SZ"
    public string Display => $"{Name} ({Code})";
}

/// <summary>
/// Fetches real-time stock prices using multiple APIs with race-condition strategy.
/// Also provides stock search/autocomplete via East Money.
/// </summary>
public class StockPriceService
{
    private static readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    static StockPriceService()
    {
        _client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    // ── Price Fetching: Race multiple APIs ──────────────

    /// <summary>
    /// Fetch prices for multiple stocks using all available APIs.
    /// First successful response from any API wins.
    /// </summary>
    public async Task<List<StockPrice>> FetchPricesAsync(List<StockEntry> stocks)
    {
        if (stocks.Count == 0) return new List<StockPrice>();

        // Build code list for batch APIs (Sina, Tencent)
        string codeList = string.Join(",", stocks.Select(s => s.Code));

        // Launch all API calls in parallel; first one to return valid data wins
        var tasks = new List<Task<List<StockPrice>?>>
        {
            FetchSinaAsync(codeList, stocks),
            FetchTencentAsync(codeList, stocks),
            FetchEastMoneyBatchAsync(stocks)
        };

        // Wait for the first successful result
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            try
            {
                var result = await completed;
                if (result != null && result.Count > 0 && result.Any(p => p.CurrentPrice > 0))
                {
                    Debug.WriteLine($"Price API: winner found with {result.Count} stock(s)");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Price API error: {ex.Message}");
            }
        }

        return new List<StockPrice>();
    }

    // ── API 1: Sina Finance ─────────────────────────────

    private async Task<List<StockPrice>?> FetchSinaAsync(string codeList, List<StockEntry> stocks)
    {
        try
        {
            string url = $"http://hq.sinajs.cn/list={codeList}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", "https://finance.sina.com.cn/");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            string text = Encoding.GetEncoding("GB2312").GetString(bytes);

            var results = new List<StockPrice>();
            foreach (var stock in stocks)
            {
                string pattern = $@"var hq_str_{stock.Code}=""([^""]*)""";
                var match = Regex.Match(text, pattern);
                if (!match.Success) continue;

                string[] fields = match.Groups[1].Value.Split(',');
                if (fields.Length < 4) continue;

                decimal price = ParseDecimal(fields, 3);
                if (price <= 0) continue;

                results.Add(new StockPrice
                {
                    Code = stock.Code,
                    Name = fields[0],
                    Open = ParseDecimal(fields, 1),
                    YesterdayClose = ParseDecimal(fields, 2),
                    CurrentPrice = price,
                    High = ParseDecimal(fields, 4),
                    Low = ParseDecimal(fields, 5),
                });
            }
            if (results.Any()) return results;
        }
        catch (Exception ex) { Debug.WriteLine($"Sina API: {ex.Message}"); }
        return null;
    }

    // ── API 2: Tencent Finance ──────────────────────────

    private async Task<List<StockPrice>?> FetchTencentAsync(string codeList, List<StockEntry> stocks)
    {
        try
        {
            string url = $"http://qt.gtimg.cn/q={codeList}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            string text = Encoding.GetEncoding("GB2312").GetString(bytes);

            var results = new List<StockPrice>();
            foreach (var stock in stocks)
            {
                string pattern = $@"v_{stock.Code}=""([^""]*)""";
                var match = Regex.Match(text, pattern);
                if (!match.Success) continue;

                string[] fields = match.Groups[1].Value.Split('~');
                if (fields.Length < 10) continue;

                decimal price = ParseDecimal(fields, 3);
                if (price <= 0) continue;

                results.Add(new StockPrice
                {
                    Code = stock.Code,
                    Name = fields[1],
                    CurrentPrice = price,
                    YesterdayClose = ParseDecimal(fields, 4),
                    Open = ParseDecimal(fields, 5),
                    High = ParseDecimal(fields, 33),
                    Low = ParseDecimal(fields, 34),
                });
            }
            if (results.Any()) return results;
        }
        catch (Exception ex) { Debug.WriteLine($"Tencent API: {ex.Message}"); }
        return null;
    }

    // ── API 3: East Money (东方财富) ─────────────────────

    // secid mapping: sh → 1, sz → 0
    private async Task<List<StockPrice>?> FetchEastMoneyBatchAsync(List<StockEntry> stocks)
    {
        try
        {
            var results = new List<StockPrice>();
            var fetchTasks = stocks.Select(s => FetchEastMoneySingleAsync(s));
            var allResults = await Task.WhenAll(fetchTasks);

            foreach (var r in allResults)
                if (r != null) results.Add(r);

            if (results.Any()) return results;
        }
        catch (Exception ex) { Debug.WriteLine($"EastMoney API: {ex.Message}"); }
        return null;
    }

    private async Task<StockPrice?> FetchEastMoneySingleAsync(StockEntry stock)
    {
        try
        {
            string market = stock.Code.StartsWith("sh") ? "1" : "0";
            string numCode = stock.Code.Substring(2);
            string secid = $"{market}.{numCode}";

            string url = $"https://push2.eastmoney.com/api/qt/stock/get" +
                $"?secid={secid}" +
                $"&fields=f43,f44,f45,f46,f47,f48,f57,f58,f60,f169,f170";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            if (data.ValueKind == JsonValueKind.Null) return null;

            decimal price = GetDecimal(data, "f43") / 100m; // f43 is price * 100
            if (price <= 0) return null;

            return new StockPrice
            {
                Code = stock.Code,
                Name = GetString(data, "f58"),
                CurrentPrice = price,
                YesterdayClose = GetDecimal(data, "f60") / 100m,
                Open = GetDecimal(data, "f46") / 100m,
                High = GetDecimal(data, "f44") / 100m,
                Low = GetDecimal(data, "f45") / 100m,
            };
        }
        catch (Exception ex) { Debug.WriteLine($"EastMoney single {stock.Code}: {ex.Message}"); }
        return null;
    }

    // ── Stock Search / Autocomplete ─────────────────────

    /// <summary>
    /// Search stocks by keyword (name or code) for autocomplete.
    /// Uses East Money suggest API.
    /// </summary>
    public async Task<List<StockSuggestion>> SearchStocksAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 1)
            return new List<StockSuggestion>();

        try
        {
            // East Money search suggest API
            string url = $"https://searchadapter.eastmoney.com/api/suggest/get" +
                $"?input={Uri.EscapeDataString(keyword)}" +
                $"&type=14" +
                $"&token=D43BF722C8E33BDC906FB84D85E326E8" +
                $"&count=10";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await _client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);

            var results = new List<StockSuggestion>();
            if (!doc.RootElement.TryGetProperty("QuotationCodeTable", out var table) ||
                table.ValueKind != JsonValueKind.Object)
                return results;

            if (!table.TryGetProperty("Data", out var dataArray) ||
                dataArray.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in dataArray.EnumerateArray())
            {
                string? name = GetString(item, "Name");
                string? code = GetString(item, "Code");
                string? market = GetString(item, "MktNum");

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
                    continue;

                // Filter: only A-share stocks (SH=1, SZ=0)
                if (market != "1" && market != "0") continue;

                string prefix = market == "1" ? "sh" : "sz";
                string fullCode = prefix + code;

                results.Add(new StockSuggestion
                {
                    Name = name,
                    Code = fullCode,
                    Market = market == "1" ? "SH" : "SZ"
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Search API error: {ex.Message}");
            return new List<StockSuggestion>();
        }
    }

    // ── Helpers ─────────────────────────────────────────

    private decimal ParseDecimal(string[] fields, int index)
    {
        if (index < fields.Length && decimal.TryParse(fields[index], out decimal val))
            return val;
        return 0;
    }

    private decimal GetDecimal(JsonElement elem, string prop)
    {
        if (elem.TryGetProperty(prop, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number)
                return val.GetDecimal();
            if (val.ValueKind == JsonValueKind.String &&
                decimal.TryParse(val.GetString(), out decimal d))
                return d;
        }
        return 0;
    }

    private string GetString(JsonElement elem, string prop)
    {
        if (elem.TryGetProperty(prop, out var val))
        {
            if (val.ValueKind == JsonValueKind.String)
                return val.GetString() ?? "";
            if (val.ValueKind == JsonValueKind.Number)
                return val.GetRawText();
        }
        return "";
    }
}
