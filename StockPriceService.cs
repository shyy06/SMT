using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SMT;

/// <summary>
/// Stock/ETF price data returned from API.
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
/// Stock/ETF search suggestion item.
/// </summary>
public class StockSuggestion
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // e.g. "sh600519"
    public string Market { get; set; } = string.Empty; // "SH" or "SZ"
    public string Type { get; set; } = string.Empty;   // "stock" or "etf"
    public string Display
    {
        get
        {
            string tag = Type == "etf" ? "[ETF]" : "";
            return $"{tag}{Name} ({Code})";
        }
    }
}

/// <summary>
/// Fetches real-time stock/ETF prices using 4 APIs with race-condition strategy:
/// 1. 新浪财经 (Sina)  2. 同花顺 (10jqka)  3. 腾讯财经 (Tencent)  4. 东方财富 (EastMoney)
/// Also provides stock/ETF search autocomplete via East Money.
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

    // ══════════════════════════════════════════════════════
    //  Main API: Race 3 sources, first valid result wins
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Fetch prices for multiple stocks/ETFs using 4 competing APIs.
    /// First successful response from any API wins.
    /// </summary>
    public async Task<List<StockPrice>> FetchPricesAsync(List<StockEntry> stocks)
    {
        if (stocks.Count == 0) return new List<StockPrice>();

        Debug.WriteLine($"[SMT] Fetching prices for {stocks.Count} stock(s) via 4-API race...");

        // Launch all 4 API calls in parallel; first one to return valid data wins
        var tasks = new List<Task<List<StockPrice>?>>
        {
            FetchSinaAsync(stocks),          // 新浪财经
            FetchTHSAsync(stocks),           // 同花顺
            FetchTencentAsync(stocks),       // 腾讯财经
            FetchEastMoneyBatchAsync(stocks) // 东方财富
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
                    Debug.WriteLine($"[SMT] Winner: got {result.Count} price(s)");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMT] API error: {ex.Message}");
            }
        }

        Debug.WriteLine("[SMT] All 4 APIs failed");
        return new List<StockPrice>();
    }

    // ══════════════════════════════════════════════════════
    //  API 1: 新浪财经 (Sina Finance) — hq.sinajs.cn
    // ══════════════════════════════════════════════════════

    private async Task<List<StockPrice>?> FetchSinaAsync(List<StockEntry> stocks)
    {
        try
        {
            string codeList = string.Join(",", stocks.Select(s => s.Code));
            string url = $"http://hq.sinajs.cn/list={codeList}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", "https://finance.sina.com.cn/");
            var response = await _client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            string text = Encoding.GetEncoding("GB2312").GetString(bytes);

            var results = new List<StockPrice>();
            foreach (var stock in stocks)
            {
                // Response format: var hq_str_sh600519="名称,今开,昨收,当前价,最高,最低,...";
                string pattern = $@"hq_str_{stock.Code}=""([^""]*)""";
                var match = Regex.Match(text, pattern);
                if (!match.Success) continue;

                string[] fields = match.Groups[1].Value.Split(',');
                if (fields.Length < 6) continue;

                // Sina field mapping:
                //  0 = 股票名称
                //  1 = 今开盘
                //  2 = 昨收盘
                //  3 = 当前价
                //  4 = 最高价
                //  5 = 最低价
                decimal price = ParseDecimal(fields, 3);
                if (price <= 0) continue;

                results.Add(new StockPrice
                {
                    Code = stock.Code,
                    Name = fields[0],
                    CurrentPrice = price,
                    YesterdayClose = ParseDecimal(fields, 2),
                    Open = ParseDecimal(fields, 1),
                    High = ParseDecimal(fields, 4),
                    Low = ParseDecimal(fields, 5),
                });
            }
            if (results.Any()) return results;
        }
        catch (Exception ex) { Debug.WriteLine($"[新浪] Error: {ex.Message}"); }
        return null;
    }

    // ══════════════════════════════════════════════════════
    //  API 2: 同花顺 (10jqka) — d.10jqka.com.cn
    // ══════════════════════════════════════════════════════

    private async Task<List<StockPrice>?> FetchTHSAsync(List<StockEntry> stocks)
    {
        try
        {
            var fetchTasks = stocks.Select(s => FetchTHSSingleAsync(s));
            var allResults = await Task.WhenAll(fetchTasks);

            var results = allResults.Where(r => r != null).Select(r => r!).ToList();
            if (results.Any()) return results;
        }
        catch (Exception ex) { Debug.WriteLine($"[同花顺] Batch error: {ex.Message}"); }
        return null;
    }

    private async Task<StockPrice?> FetchTHSSingleAsync(StockEntry stock)
    {
        try
        {
            // URL: https://d.10jqka.com.cn/v2/realhead/hs_{code}/last.js
            // e.g. hs_600519 for 贵州茅台, hs_510050 for ETF
            string numCode = stock.Code.Substring(2);
            string url = $"https://d.10jqka.com.cn/v2/realhead/hs_{numCode}/last.js";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            string text = await response.Content.ReadAsStringAsync(cts.Token);

            // Response is JSONP: quotebridge_v2_realhead_hs_xxx_last({...})
            // Extract the JSON object from inside the callback
            int braceStart = text.IndexOf('{');
            int braceEnd = text.LastIndexOf('}');
            if (braceStart < 0 || braceEnd < 0) return null;

            string json = text.Substring(braceStart, braceEnd - braceStart + 1);
            using var doc = JsonDocument.Parse(json);

            // Name is at root level
            string name = GetString(doc.RootElement, "name");
            if (string.IsNullOrEmpty(name)) return null;

            // Check stock status: skip if halted
            string status = GetString(doc.RootElement, "stockStatus");
            if (status == "停牌") return null;

            // Items contain the numeric fields
            if (!doc.RootElement.TryGetProperty("items", out var items))
                return null;

            // Field mapping (empirically verified):
            //  7 = current price (最新价)
            // 10 = yesterday close (昨收)
            // 30 = today open (今开)
            //  8 = day high (最高)
            //  9 = day low (最低)
            decimal price = GetDecimal(items, "7");
            if (price <= 0) return null;

            return new StockPrice
            {
                Code = stock.Code,
                Name = name,
                CurrentPrice = price,
                YesterdayClose = GetDecimal(items, "10"),
                Open = GetDecimal(items, "30"),
                High = GetDecimal(items, "8"),
                Low = GetDecimal(items, "9"),
            };
        }
        catch (Exception ex) { Debug.WriteLine($"[同花顺] {stock.Code}: {ex.Message}"); }
        return null;
    }

    // ══════════════════════════════════════════════════════
    //  API 3: 腾讯财经 (Tencent Finance) — qt.gtimg.cn
    // ══════════════════════════════════════════════════════

    private async Task<List<StockPrice>?> FetchTencentAsync(List<StockEntry> stocks)
    {
        try
        {
            string codeList = string.Join(",", stocks.Select(s => s.Code));
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
        catch (Exception ex) { Debug.WriteLine($"[腾讯] Error: {ex.Message}"); }
        return null;
    }

    // ══════════════════════════════════════════════════════
    //  API 4: 东方财富 (East Money) — push2.eastmoney.com
    // ══════════════════════════════════════════════════════

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
        catch (Exception ex) { Debug.WriteLine($"[东方财富] Batch error: {ex.Message}"); }
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

            decimal price = GetDecimal(data, "f43") / 100m;
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
        catch (Exception ex) { Debug.WriteLine($"[东方财富] {stock.Code}: {ex.Message}"); }
        return null;
    }

    // ══════════════════════════════════════════════════════
    //  Stock / ETF Search / Autocomplete
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Search stocks and ETFs by keyword (name or code) for autocomplete.
    /// Uses East Money suggest API (type=14 includes stocks, funds, ETFs).
    /// </summary>
    public async Task<List<StockSuggestion>> SearchStocksAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 1)
            return new List<StockSuggestion>();

        try
        {
            string url = $"https://searchadapter.eastmoney.com/api/suggest/get" +
                $"?input={Uri.EscapeDataString(keyword)}" +
                $"&type=14" +
                $"&token=D43BF722C8E33BDC906FB84D85E326E8" +
                $"&count=15";

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
                string? secType = GetString(item, "SecurityTypeName");

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
                    continue;

                // Only A-share stocks (market 0/1) and ETFs (market 0/1 with type info)
                if (market != "1" && market != "0") continue;

                string prefix = market == "1" ? "sh" : "sz";
                string fullCode = prefix + code;

                // Detect ETF type
                bool isETF = false;
                if (!string.IsNullOrEmpty(secType) &&
                    (secType.Contains("ETF") || secType.Contains("基金")))
                    isETF = true;
                // Also detect by code prefix: SH 51xxxx = ETF, SZ 159xxx = ETF
                if (market == "1" && code.StartsWith("51")) isETF = true;
                if (market == "0" && code.StartsWith("159")) isETF = true;

                results.Add(new StockSuggestion
                {
                    Name = name,
                    Code = fullCode,
                    Market = market == "1" ? "SH" : "SZ",
                    Type = isETF ? "etf" : "stock"
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[搜索] Error: {ex.Message}");
            return new List<StockSuggestion>();
        }
    }

    // ══════════════════════════════════════════════════════
    //  JSON / String Helpers
    // ══════════════════════════════════════════════════════

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
