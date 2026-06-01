using System.Text;
using System.Text.Json;

namespace GuestGate.Api.Services;

internal static class GuestFormBuilder
{
    public static string BuildGuestFormConfigJson(string tplId, string templateJson, string prefillJson)
    {
        var safeTemplate = FilterTemplateForGuest(templateJson);
        var safePrefill = FilterJsonObjectByGuestVisibleFields(prefillJson, templateJson);
        return JsonSerializer.Serialize(new
        {
            templateId = tplId,
            template = JsonDocument.Parse(safeTemplate).RootElement,
            prefill = JsonDocument.Parse(safePrefill).RootElement
        });
    }

    public static string FilterSubmittedGuestData(JsonElement submittedData, string templateJson)
    {
        return FilterJsonElementByGuestVisibleFields(submittedData, templateJson);
    }

    private static string FilterTemplateForGuest(string templateJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(templateJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "{}";

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("fields") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WritePropertyName(prop.Name);
                        writer.WriteStartArray();
                        foreach (var field in prop.Value.EnumerateArray())
                        {
                            if (field.ValueKind == JsonValueKind.Object && IsStartFormField(field) && IsGuestVisibleField(field))
                            {
                                field.WriteTo(writer);
                            }
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            // Fail closed for guest-facing form config so hidden template fields are not leaked.
            return "{\"fields\":[]}";
        }
    }

    private static string FilterJsonObjectByGuestVisibleFields(string json, string templateJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return FilterJsonElementByGuestVisibleFields(doc.RootElement, templateJson);
        }
        catch
        {
            return "{}";
        }
    }

    private static string FilterJsonElementByGuestVisibleFields(JsonElement data, string templateJson)
    {
        try
        {
            var visibleKeys = GetGuestVisibleKeys(templateJson);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                if (data.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in data.EnumerateObject())
                    {
                        if (visibleKeys.Contains(prop.Name))
                        {
                            prop.WriteTo(writer);
                        }
                    }
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            // Fail closed: never store manually-submitted fields that are not allowed by the template.
            return "{}";
        }
    }

    private static HashSet<string> GetGuestVisibleKeys(string templateJson)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(templateJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return keys;
        if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array) return keys;

        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object) continue;
            if (!IsStartFormField(field) || !IsGuestVisibleField(field)) continue;
            var key = GetFieldKey(field);
            if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
        }

        return keys;
    }

    private static bool IsStartFormField(JsonElement field)
    {
        if (!field.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String) return true;
        var value = scope.GetString();
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "StartForm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGuestVisibleField(JsonElement field)
    {
        if (field.TryGetProperty("visible", out var rootVisible) && IsJsonFalse(rootVisible)) return false;

        if (!field.TryGetProperty("guest", out var guest) || guest.ValueKind != JsonValueKind.Object) return true;
        if (guest.TryGetProperty("hide", out var hide) && IsJsonTrue(hide)) return false;
        if (guest.TryGetProperty("visible", out var visible) && IsJsonFalse(visible)) return false;
        return true;
    }

    private static bool IsJsonFalse(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.False ||
               (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsJsonTrue(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetFieldKey(JsonElement field)
    {
        foreach (var propertyName in new[] { "key", "name", "id" })
        {
            if (field.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return null;
    }
}
