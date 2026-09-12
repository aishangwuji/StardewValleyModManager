using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SVL.Core.Platform.Modpack;

/// <summary>Curseforge 整合包 manifest.json 模型（从 SVL.Core.Modpack 下沉）。</summary>
public sealed class CurseforgeModpackManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("minecraftVersion")]
    public string MinecraftVersion { get; set; } = string.Empty;

    [JsonPropertyName("manifestVersion")]
    [JsonConverter(typeof(FlexibleInt32JsonConverter))]
    public int ManifestVersion { get; set; }

    [JsonPropertyName("files")]
    private List<CurseforgeModpackFile> _files = new();

    /// <summary>允许部分导出器把无 Mod 的整合包写成 files: null。</summary>
    public List<CurseforgeModpackFile> Files
    {
        get => _files;
        set => _files = value ?? new();
    }

    [JsonPropertyName("overrides")]
    public string? Overrides { get; set; }
}

/// <summary>Curseforge 整合包中的单个文件条目。</summary>
public sealed class CurseforgeModpackFile
{
    [JsonPropertyName("projectID")]
    [JsonConverter(typeof(FlexibleInt64JsonConverter))]
    public long ProjectId { get; set; }

    [JsonPropertyName("fileID")]
    [JsonConverter(typeof(FlexibleInt64JsonConverter))]
    public long FileId { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}

/// <summary>兼容部分导出工具把 CurseForge ID 写成 JSON 字符串的情况。</summary>
internal sealed class FlexibleInt64JsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String &&
            long.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return 0;
        }

        throw new JsonException("CurseForge 项目/文件 ID 不是有效的整数");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>兼容部分导出工具把 CurseForge manifestVersion 写成数字字符串。</summary>
internal sealed class FlexibleInt32JsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String &&
            int.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return 0;
        }

        throw new JsonException("CurseForge manifestVersion 不是有效的整数");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Curseforge 整合包解析器（精简版）。从 SVL.Core.Modpack.CurseforgeModpackParser 下沉，
/// 用 System.Text.Json + System.IO.Compression 替代 SharpZipLib。
/// </summary>
public static class CurseforgeModpackParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>从已解压的 manifest.json 文件解析。</summary>
    public static CurseforgeModpackManifest ParseFromJsonFile(string manifestJsonPath)
    {
        if (!File.Exists(manifestJsonPath))
        {
            throw new FileNotFoundException($"manifest.json 文件不存在: {manifestJsonPath}");
        }

        using var reader = new StreamReader(
            manifestJsonPath,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var jsonContent = reader.ReadToEnd();
        return JsonSerializer.Deserialize<CurseforgeModpackManifest>(jsonContent, JsonOptions)
            ?? throw new InvalidOperationException("无法解析 manifest.json");
    }

    /// <summary>直接从 zip 文件中读取 manifest.json（用于不经临时解压的快速校验场景）。</summary>
    public static CurseforgeModpackManifest? TryParseFromZip(string zipFilePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipFilePath);
            var manifestEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry == null)
            {
                return null;
            }

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var jsonContent = reader.ReadToEnd();
            return JsonSerializer.Deserialize<CurseforgeModpackManifest>(jsonContent, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>获取默认的实例名称。</summary>
    public static string GetDefaultInstanceName(CurseforgeModpackManifest manifest)
    {
        return $"{manifest.Name} {manifest.Version}".Trim();
    }
}
