using BenchmarkDotNet.Attributes;
using System.Text;

namespace MainAPP.Benchmarks.Benchmarks;

/// <summary>
/// 字符串处理性能基准测试，聚焦 CSV 导出热点。
/// BarcodeDataService.CsvEscape 为 private，此处复制实现作为基准，
/// 对比不同转义策略的性能差异，为后续优化提供数据支撑。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class StringProcessingBenchmarks
{
    private string _simpleField = null!;
    private string _fieldWithComma = null!;
    private string _fieldWithQuote = null!;
    private string _fieldWithNewline = null!;
    private string _fieldAllSpecial = null!;
    private string _csvLine = null!;

    [Params(10, 100, 1000)]
    public int FieldCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simpleField = "ABC1234567890";
        _fieldWithComma = "ABC,123,456";
        _fieldWithQuote = "ABC\"123\"456";
        _fieldWithNewline = "ABC\n123\r456";
        _fieldAllSpecial = "ABC,\"123\n456\r789\"";

        // 构造一条 CSV 行用于解析测试
        var sb = new StringBuilder();
        for (int i = 0; i < FieldCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape_ContainsCheck($"Field_{i},with\"quotes\n"));
        }
        _csvLine = sb.ToString();
    }

    [Benchmark(Description = "CsvEscape - 简单字段(无特殊字符)")]
    public string CsvEscape_Simple() => CsvEscape_ContainsCheck(_simpleField);

    [Benchmark(Description = "CsvEscape - 含逗号")]
    public string CsvEscape_Comma() => CsvEscape_ContainsCheck(_fieldWithComma);

    [Benchmark(Description = "CsvEscape - 含引号")]
    public string CsvEscape_Quote() => CsvEscape_ContainsCheck(_fieldWithQuote);

    [Benchmark(Description = "CsvEscape - 含换行")]
    public string CsvEscape_Newline() => CsvEscape_ContainsCheck(_fieldWithNewline);

    [Benchmark(Description = "CsvEscape - 全部特殊字符")]
    public string CsvEscape_AllSpecial() => CsvEscape_ContainsCheck(_fieldAllSpecial);

    [Benchmark(Description = "ParseCsvLine - 解析整行")]
    public int ParseCsvLine_Full()
    {
        var fields = ParseCsvLine(_csvLine);
        return fields.Length;
    }

    /// <summary>
    /// 复制自 BarcodeDataService.CsvEscape 的实现（Contains 检查策略）
    /// </summary>
    private static string CsvEscape_ContainsCheck(string? field)
    {
        field ??= string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    /// <summary>
    /// 复制自 BarcodeDataService.ParseCSVLine 的实现
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        result.Add(currentField.ToString());
        return result.ToArray();
    }
}
