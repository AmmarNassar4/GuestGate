#!/usr/bin/env python3
"""
Apply GuestGate guest-visible filtering fix.

Run from the repository root:
  py .\apply_visible_false_fix.py .

What it changes:
- Server filters /tablet/{kid}/form-config and /api/mobile/form-config so guest.visible=false fields are not sent.
- Server filters /api/mobile/save so hidden fields cannot be submitted manually.
- index.html and mobile.html also hide guest.visible=false as a client-side fallback.
"""
from __future__ import annotations

import datetime as _dt
import re
import shutil
import sys
from pathlib import Path


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def write_text(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def backup_file(repo: Path, path: Path, backup_root: Path) -> None:
    rel = path.relative_to(repo)
    dst = backup_root / rel
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, dst)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"Could not find expected block for {label}.")
    return text.replace(old, new, 1)


def patch_program(repo: Path, backup_root: Path) -> list[str]:
    path = repo / "src" / "GuestGate.Api" / "Program.cs"
    if not path.exists():
        raise FileNotFoundError(path)
    backup_file(repo, path, backup_root)
    text = read_text(path)
    changed: list[str] = []

    raw_return = 'return Results.Content($@"{{""templateId"":""{tplId}"",""template"":{t.DataJson},""prefill"":{prefill}}}", "application/json");'
    filtered_return = 'return Results.Content(BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");'
    count = text.count(raw_return)
    if count:
        text = text.replace(raw_return, filtered_return)
        changed.append(f"Program.cs: replaced {count} form-config response(s) with guest-filtered JSON.")
    elif filtered_return in text:
        changed.append("Program.cs: form-config responses already use BuildGuestFormConfigJson().")
    else:
        raise RuntimeError("Could not locate form-config response block in Program.cs.")

    raw_save = '    Guest? guest = null;\n\n    guest = new Guest { DataJson = body.data.GetRawText(), CreatedAt = now, UpdatedAt = now };'
    filtered_save = '''    var saveTplId = s.TemplateId ?? "T1";
    var saveTemplate = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == saveTplId);
    if (saveTemplate is null) return Results.NotFound(new { error = $"Template '{saveTplId}' not found" });
    var guestDataJson = FilterSubmittedGuestData(body.data, saveTemplate.DataJson);

    Guest? guest = null;

    guest = new Guest { DataJson = guestDataJson, CreatedAt = now, UpdatedAt = now };'''
    if raw_save in text:
        text = text.replace(raw_save, filtered_save, 1)
        changed.append("Program.cs: /api/mobile/save now removes hidden guest fields before saving.")
    elif "FilterSubmittedGuestData(body.data" in text:
        changed.append("Program.cs: /api/mobile/save already filters submitted fields.")
    else:
        raise RuntimeError("Could not locate mobile save block in Program.cs.")

    helper_marker = "static string BuildGuestFormConfigJson("
    helpers = r'''
static string BuildGuestFormConfigJson(string tplId, string templateJson, string prefillJson)
{
    var safeTemplate = FilterTemplateForGuest(templateJson);
    var safePrefill = FilterJsonObjectByGuestVisibleFields(prefillJson, templateJson);
    return $"{{\"templateId\":{JsonSerializer.Serialize(tplId)},\"template\":{safeTemplate},\"prefill\":{safePrefill}}}";
}

static string FilterTemplateForGuest(string templateJson)
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

static string FilterJsonObjectByGuestVisibleFields(string json, string templateJson)
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

static string FilterSubmittedGuestData(JsonElement submittedData, string templateJson)
{
    return FilterJsonElementByGuestVisibleFields(submittedData, templateJson);
}

static string FilterJsonElementByGuestVisibleFields(JsonElement data, string templateJson)
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

static HashSet<string> GetGuestVisibleKeys(string templateJson)
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

static bool IsStartFormField(JsonElement field)
{
    if (!field.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String) return true;
    var value = scope.GetString();
    return string.IsNullOrWhiteSpace(value) || string.Equals(value, "StartForm", StringComparison.OrdinalIgnoreCase);
}

static bool IsGuestVisibleField(JsonElement field)
{
    if (field.TryGetProperty("visible", out var rootVisible) && IsJsonFalse(rootVisible)) return false;

    if (!field.TryGetProperty("guest", out var guest) || guest.ValueKind != JsonValueKind.Object) return true;
    if (guest.TryGetProperty("hide", out var hide) && IsJsonTrue(hide)) return false;
    if (guest.TryGetProperty("visible", out var visible) && IsJsonFalse(visible)) return false;
    return true;
}

static bool IsJsonFalse(JsonElement value)
{
    return value.ValueKind == JsonValueKind.False ||
           (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase));
}

static bool IsJsonTrue(JsonElement value)
{
    return value.ValueKind == JsonValueKind.True ||
           (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));
}

static string? GetFieldKey(JsonElement field)
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

'''
    if helper_marker not in text:
        marker = "static async Task EnsureConsentRequestsTableAsync(AppDb db)"
        if marker not in text:
            raise RuntimeError("Could not locate static helper insertion point in Program.cs.")
        text = text.replace(marker, helpers + marker, 1)
        changed.append("Program.cs: added guest visibility helper functions.")
    else:
        changed.append("Program.cs: guest visibility helper functions already exist.")

    write_text(path, text)
    return changed


def patch_mobile(repo: Path, backup_root: Path) -> list[str]:
    path = repo / "src" / "GuestGate.Api" / "wwwroot" / "mobile.html"
    if not path.exists():
        raise FileNotFoundError(path)
    backup_file(repo, path, backup_root)
    text = read_text(path)
    changed: list[str] = []

    old = "  function isHidden(field){ return !!(field.guest && field.guest.hide === true); }"
    new = """  function isGuestVisible(field){
    const g = field && field.guest;
    if(!g) return true;
    return !(g.hide === true || g.visible === false || String(g.visible).toLowerCase() === 'false');
  }
  function isHidden(field){ return !isGuestVisible(field); }"""
    if old in text:
        text = text.replace(old, new, 1)
        changed.append("mobile.html: guest.visible=false is now hidden in the mobile form fallback.")
    elif "function isGuestVisible(field)" in text and "function isHidden(field){ return !isGuestVisible(field); }" in text:
        changed.append("mobile.html: guest.visible=false fallback already exists.")
    else:
        raise RuntimeError("Could not locate isHidden() in mobile.html.")

    write_text(path, text)
    return changed


def patch_index(repo: Path, backup_root: Path) -> list[str]:
    path = repo / "src" / "GuestGate.Api" / "wwwroot" / "index.html"
    if not path.exists():
        raise FileNotFoundError(path)
    backup_file(repo, path, backup_root)
    text = read_text(path)
    changed: list[str] = []

    if "function isGuestVisible(field)" not in text:
        marker = "            async function renderInlineForm() {"
        helper = """            function isGuestVisible(field) {
                const g = field && field.guest;
                if (!g) return true;
                return !(g.hide === true || g.visible === false || String(g.visible).toLowerCase() === 'false');
            }

"""
        if marker not in text:
            raise RuntimeError("Could not locate renderInlineForm() in index.html.")
        text = text.replace(marker, helper + marker, 1)
        changed.append("index.html: added guest.visible=false fallback helper.")
    else:
        changed.append("index.html: guest.visible=false helper already exists.")

    # Add the filter directly before the scope filter, matching current formatting.
    old = """                const fields = (template.fields || [])
                    .filter(f => !f.scope || f.scope === 'StartForm')
                    .sort((a, b) => (a.order || 0) - (b.order || 0));"""
    new = """                const fields = (template.fields || [])
                    .filter(isGuestVisible)
                    .filter(f => !f.scope || f.scope === 'StartForm')
                    .sort((a, b) => (a.order || 0) - (b.order || 0));"""
    if old in text:
        text = text.replace(old, new, 1)
        changed.append("index.html: inline tablet form now filters guest.visible=false.")
    elif ".filter(isGuestVisible)" in text:
        changed.append("index.html: inline tablet filter already includes isGuestVisible.")
    else:
        # More flexible fallback for small formatting changes.
        text2, n = re.subn(
            r"(const\s+fields\s*=\s*\(template\.fields\s*\|\|\s*\[\]\)\s*\n)(\s*\.filter\(f\s*=>\s*!f\.scope\s*\|\|\s*f\.scope\s*===\s*'StartForm'\))",
            r"\1                    .filter(isGuestVisible)\n\2",
            text,
            count=1,
        )
        if n:
            text = text2
            changed.append("index.html: inline tablet form now filters guest.visible=false.")
        else:
            raise RuntimeError("Could not locate fields filter in index.html.")

    write_text(path, text)
    return changed


def main() -> int:
    repo = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    if not (repo / "src" / "GuestGate.Api").exists():
        print(f"ERROR: {repo} does not look like the GuestGate repository root.", file=sys.stderr)
        return 2

    stamp = _dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_root = repo / ".guestgate-visible-false-backup" / stamp
    backup_root.mkdir(parents=True, exist_ok=True)

    changes: list[str] = []
    try:
        changes.extend(patch_program(repo, backup_root))
        changes.extend(patch_mobile(repo, backup_root))
        changes.extend(patch_index(repo, backup_root))
    except Exception as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        print(f"Backup folder: {backup_root}", file=sys.stderr)
        return 1

    print("GuestGate visible=false fix applied.")
    print(f"Backup folder: {backup_root}")
    print("Changes:")
    for item in changes:
        print(f"- {item}")
    print("\nNext steps:")
    print("1) dotnet build .\\GuestGate.sln")
    print("2) Run the API")
    print("3) Ctrl+F5 on tablet and mobile pages")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
