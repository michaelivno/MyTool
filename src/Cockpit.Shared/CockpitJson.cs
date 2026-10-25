using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cockpit.Shared;

/// <summary>Single source of truth for JSON settings shared by the API, the client and the export format.</summary>
public static class CockpitJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.DictionaryKeyPolicy = null; // keep custom-field keys exactly as typed
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        o.WriteIndented = false;
        return o;
    }

    public static JsonSerializerOptions Indented()
    {
        var o = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        o.WriteIndented = true;
        return o;
    }
}
