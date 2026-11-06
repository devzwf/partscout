using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartScout.Api.Infrastructure;

public static class Json
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        if (!o.Converters.OfType<JsonStringEnumConverter>().Any()) o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
