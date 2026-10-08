using System.Text.Json;
using System.Text.Json.Serialization;

namespace MrtRouteSimulator.Automation;

public static class AutomationJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}
