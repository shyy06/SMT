namespace SMT;

using System.Text;

/// <summary>
/// Represents a single stock entry in the config file.
/// </summary>
public class StockEntry
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // e.g. "sh600519" or "sz000001"
}

/// <summary>
/// Loads/saves stock list from/to config.txt in the application directory.
/// Format: one stock per line: Name,Code
/// Example:
///   贵州茅台,sh600519
///   中国平安,sz000001
/// Lines starting with # are comments.
/// </summary>
public static class StockConfig
{
    /// <summary>
    /// Full path to config.txt (next to SMT.exe).
    /// </summary>
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    /// <summary>
    /// Load stock list from config.txt.
    /// If the file doesn't exist, create a default one with sample stocks.
    /// </summary>
    public static List<StockEntry> Load()
    {
        var stocks = new List<StockEntry>();

        if (!File.Exists(ConfigPath))
        {
            // Create default config with sample stocks
            var defaultLines = new[]
            {
                "# SMT 股票盯盘配置文件",
                "# 格式：股票名称,股票代码（sh/sz开头）",
                "# 示例：贵州茅台,sh600519",
                "# 修改后重启软件生效，或通过右键菜单管理",
                "贵州茅台,sh600519",
                "中国平安,sz000001",
            };
            File.WriteAllLines(ConfigPath, defaultLines, Encoding.UTF8);
        }

        var lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            var parts = trimmed.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
            {
                stocks.Add(new StockEntry
                {
                    Name = parts[0],
                    Code = FormatStockCode(parts[1])
                });
            }
        }

        return stocks;
    }

    /// <summary>
    /// Save stock list to config.txt.
    /// </summary>
    public static void Save(List<StockEntry> stocks)
    {
        var lines = new List<string>
        {
            "# SMT 股票盯盘配置文件",
            "# 格式：股票名称,股票代码（sh/sz开头）",
            "# 示例：贵州茅台,sh600519",
            "# 修改后重启软件生效，或通过右键菜单管理",
            ""
        };

        foreach (var s in stocks)
        {
            lines.Add($"{s.Name},{s.Code}");
        }

        File.WriteAllLines(ConfigPath, lines, Encoding.UTF8);
    }

    /// <summary>
    /// Normalize stock code to sh/sz prefix format.
    /// Accepts: "600519", "sh600519", "SH600519", "贵州茅台"
    /// </summary>
    public static string FormatStockCode(string input)
    {
        input = input.Trim().ToLower();

        // Already has prefix
        if (input.StartsWith("sh") || input.StartsWith("sz"))
            return input;

        // 6-digit code: determine market
        if (input.Length == 6 && input.All(char.IsDigit))
        {
            int prefix = int.Parse(input.Substring(0, 3));
            // Shanghai: 600xxx, 601xxx, 603xxx, 605xxx, 688xxx
            // Shenzhen: 000xxx, 001xxx, 002xxx, 003xxx, 300xxx, 301xxx
            if (prefix >= 600 && prefix <= 605 || prefix == 688)
                return "sh" + input;
            else
                return "sz" + input;
        }

        return input;
    }
}
