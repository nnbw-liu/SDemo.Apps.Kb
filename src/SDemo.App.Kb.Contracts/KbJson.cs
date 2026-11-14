using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDemo.App.Kb.Contracts;

/// <summary>线上 JSON 的唯一配置：camelCase、枚举小写、忽略 null。</summary>
public static class KbJson
{
    public static JsonSerializerOptions Wire { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Wire);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Wire);
}
