using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cockpit.Server.Domain;
using Cockpit.Shared;

namespace Cockpit.Server.Services;

public sealed record MarkdownFile(string Path, string Content);

/// <summary>
/// Obsidian-compatible vault: one note per area and per project (YAML frontmatter + wikilinks),
/// plus Inbox.md and one note per review. Built from the JSON export, so it is derived data.
/// </summary>
public sealed class MarkdownExportService(ExportService export, IClock clock)
{
    public async Task<byte[]> BuildZipAsync(CancellationToken ct = default)
    {
        var doc = await export.ExportAsync(ct);
        var files = Build(doc, clock.UtcNow);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in files)
            {
                var entry = zip.CreateEntry(f.Path, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(f.Content);
            }
        }
        return ms.ToArray();
    }

    public static List<MarkdownFile> Build(ExportDocument doc, DateTime exportedAt)
    {
        var areas = doc.Areas.Where(a => a.DeletedAt is null).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToList();
        var projects = doc.Projects.Where(p => p.DeletedAt is null && areas.Any(a => a.Id == p.AreaId)).ToList();
        var tasks = doc.Tasks.Where(t => t.DeletedAt is null).ToList();
        var areaById = areas.ToDictionary(a => a.Id);

        var areaTitle = UniqueTitles(areas, a => a.Name, a => a.Id);
        var projectTitle = UniqueTitles(projects, p => p.Name, p => p.Id,
            fallback: p => $"{p.Name} ({(areaById.TryGetValue(p.AreaId, out var a) ? a.Name : "?")})");

        var files = new List<MarkdownFile>();
        var sb = new StringBuilder();

        // ---- README / index ----
        sb.AppendLine("---");
        Yaml(sb, "type", "cockpit-index");
        Yaml(sb, "exported", exportedAt);
        Yaml(sb, "wip_limit", doc.Settings.TryGetValue(SettingsService.WipLimitKey, out var wip) ? wip : ProjectRules.DefaultWipLimit.ToString());
        sb.AppendLine("---");
        sb.AppendLine("# Cockpit");
        sb.AppendLine();
        sb.AppendLine($"Exported {exportedAt:yyyy-MM-dd HH:mm} UTC. {areas.Count} areas, {projects.Count} projects, {tasks.Count(t => t.Status.IsOpen())} open tasks.");
        sb.AppendLine();
        foreach (var a in areas)
        {
            var ap = projects.Where(p => p.AreaId == a.Id).ToList();
            sb.AppendLine($"- [[{areaTitle[a.Id]}]]{(a.Archived ? " (archived)" : "")} — {ap.Count(p => p.Status == ProjectStatus.Active)} active, {ap.Count(p => p.Status == ProjectStatus.Parked)} parked");
        }
        files.Add(new("README.md", sb.ToString()));

        // ---- Areas ----
        foreach (var a in areas)
        {
            sb.Clear();
            sb.AppendLine("---");
            Yaml(sb, "id", a.Id.ToString());
            Yaml(sb, "type", "area");
            Yaml(sb, "kind", Snake(a.Kind));
            Yaml(sb, "color", a.Color);
            Yaml(sb, "review_cadence", Snake(a.ReviewCadence));
            Yaml(sb, "archived", a.Archived);
            Yaml(sb, "created", a.CreatedAt);
            Yaml(sb, "updated", a.UpdatedAt);
            CustomYaml(sb, a.Properties);
            sb.AppendLine("---");
            sb.AppendLine($"# {a.Name}");
            sb.AppendLine();
            foreach (var status in new[] { ProjectStatus.Active, ProjectStatus.Parked, ProjectStatus.Done, ProjectStatus.Dropped })
            {
                var ps = projects.Where(p => p.AreaId == a.Id && p.Status == status).OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToList();
                if (ps.Count == 0) continue;
                sb.AppendLine($"## {Label(status)}");
                foreach (var p in ps)
                {
                    var extra = status == ProjectStatus.Parked && p.RevisitAt is { } r ? $" — revisit {r:yyyy-MM-dd}" : p.Deadline is { } d && !status.IsClosed() ? $" — deadline {d:yyyy-MM-dd}" : "";
                    sb.AppendLine($"- [[{projectTitle[p.Id]}]]{extra}");
                }
                sb.AppendLine();
            }
            var loose = tasks.Where(t => t.AreaId == a.Id && t.ProjectId is null).OrderBy(t => t.Status.IsOpen() ? 0 : 1).ThenBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => t.SortOrder).ToList();
            if (loose.Count > 0)
            {
                sb.AppendLine("## Tasks");
                foreach (var t in loose) sb.AppendLine(TaskLine(t));
                sb.AppendLine();
            }
            files.Add(new($"Areas/{Safe(areaTitle[a.Id])}.md", sb.ToString()));
        }

        // ---- Projects ----
        foreach (var p in projects.OrderBy(p => p.AreaId).ThenBy(p => p.SortOrder))
        {
            var a = areaById[p.AreaId];
            sb.Clear();
            sb.AppendLine("---");
            Yaml(sb, "id", p.Id.ToString());
            Yaml(sb, "type", "project");
            Yaml(sb, "area", $"[[{areaTitle[a.Id]}]]");
            Yaml(sb, "status", Snake(p.Status));
            Yaml(sb, "priority", Snake(p.Priority));
            Yaml(sb, "goal", p.Goal);
            Yaml(sb, "next_milestone", p.NextMilestone);
            Yaml(sb, "deadline", p.Deadline);
            Yaml(sb, "revisit_at", p.RevisitAt);
            Yaml(sb, "created", p.CreatedAt);
            Yaml(sb, "updated", p.UpdatedAt);
            Yaml(sb, "completed", p.CompletedAt);
            CustomYaml(sb, p.Properties);
            sb.AppendLine("---");
            sb.AppendLine($"# {p.Name}");
            sb.AppendLine();
            sb.AppendLine($"**Area:** [[{areaTitle[a.Id]}]] · **Status:** {Label(p.Status)}{(p.Deadline is { } dl ? $" · **Deadline:** {dl:yyyy-MM-dd}" : "")}{(p.Status == ProjectStatus.Parked && p.RevisitAt is { } rv ? $" · **Revisit:** {rv:yyyy-MM-dd}" : "")}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(p.Goal)) { sb.AppendLine("## Goal"); sb.AppendLine(p.Goal); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(p.NextMilestone)) { sb.AppendLine("## Next milestone"); sb.AppendLine(p.NextMilestone); sb.AppendLine(); }
            var pt = tasks.Where(t => t.ProjectId == p.Id).OrderBy(t => t.Status.IsOpen() ? 0 : 1).ThenBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => t.SortOrder).ToList();
            if (pt.Count > 0)
            {
                sb.AppendLine("## Tasks");
                foreach (var t in pt) sb.AppendLine(TaskLine(t));
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(p.Notes))
            {
                sb.AppendLine("## Notes");
                sb.AppendLine(p.Notes.TrimEnd());
                sb.AppendLine();
            }
            files.Add(new($"Projects/{Safe(areaTitle[a.Id])}/{Safe(projectTitle[p.Id])}.md", sb.ToString()));
        }

        // ---- Inbox ----
        var inbox = doc.InboxItems.Where(i => i.DeletedAt is null && i.ProcessedAt is null).OrderBy(i => i.CreatedAt).ToList();
        sb.Clear();
        sb.AppendLine("---");
        Yaml(sb, "type", "inbox");
        Yaml(sb, "count", inbox.Count);
        sb.AppendLine("---");
        sb.AppendLine("# Inbox");
        sb.AppendLine();
        foreach (var i in inbox) sb.AppendLine($"- [ ] {OneLine(i.Text)} <!-- {i.CreatedAt:yyyy-MM-dd} -->");
        if (inbox.Count == 0) sb.AppendLine("_Empty._");
        files.Add(new("Inbox.md", sb.ToString()));

        // ---- Reviews ----
        var usedReviewNames = new HashSet<string>();
        foreach (var r in doc.Reviews.Where(r => r.DeletedAt is null).OrderBy(r => r.StartedAt))
        {
            var name = $"{r.StartedAt:yyyy-MM-dd} review";
            if (!usedReviewNames.Add(name)) { name = $"{r.StartedAt:yyyy-MM-dd HHmm} review"; usedReviewNames.Add(name); }
            sb.Clear();
            sb.AppendLine("---");
            Yaml(sb, "id", r.Id.ToString());
            Yaml(sb, "type", "review");
            Yaml(sb, "status", Snake(r.Status));
            Yaml(sb, "started", r.StartedAt);
            Yaml(sb, "finished", r.FinishedAt);
            sb.AppendLine("---");
            sb.AppendLine($"# Weekly review — {r.StartedAt:yyyy-MM-dd}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(r.Notes)) { sb.AppendLine("## Notes"); sb.AppendLine(r.Notes.TrimEnd()); sb.AppendLine(); }
            if (r.Decisions.Count > 0)
            {
                sb.AppendLine("## Decisions");
                foreach (var d in r.Decisions)
                {
                    var link = projectTitle.TryGetValue(d.ProjectId, out var title) ? $"[[{title}]]" : d.ProjectName;
                    sb.AppendLine($"- {link} → {d.Decision.Replace('_', ' ')}{(string.IsNullOrWhiteSpace(d.Note) ? "" : $" — {OneLine(d.Note)}")}");
                }
                sb.AppendLine();
            }
            if (r.Snapshot is { } snap)
            {
                sb.AppendLine("## Snapshot");
                foreach (var key in new[] { "start", "end" })
                {
                    if (snap[key] is not JsonObject s) continue;
                    sb.AppendLine($"- **{key}:** {s["active"]?.AsArray().Count ?? 0} active, {s["parked"]?.AsArray().Count ?? 0} parked, {s["decide"]?.AsArray().Count ?? 0} to decide, {s["overdueTasks"]} overdue tasks, {s["inboxUnprocessed"]} in inbox");
                }
                sb.AppendLine();
            }
            files.Add(new($"Reviews/{Safe(name)}.md", sb.ToString()));
        }

        return files;
    }

    // ---- helpers ----

    private static string TaskLine(TaskDto t)
    {
        var box = t.Status switch { TaskItemStatus.Done => "[x]", TaskItemStatus.Dropped => "[-]", TaskItemStatus.Doing => "[/]", _ => "[ ]" };
        var parts = new List<string> { OneLine(t.Title) };
        if (t.Due is { } d) parts.Add($"(due {d:yyyy-MM-dd})");
        if (t.Effort is { } e) parts.Add($"[{e}]");
        foreach (var tag in t.Tags) parts.Add($"#{Safe(tag).Replace(' ', '-')}");
        return $"- {box} {string.Join(' ', parts)}";
    }

    /// <summary>Distinct note titles for wikilinks: duplicates get the fallback (e.g. "Name (Area)"), then " (2)", " (3)"…</summary>
    private static Dictionary<Guid, string> UniqueTitles<T>(IEnumerable<T> items, Func<T, string> name, Func<T, Guid> id, Func<T, string>? fallback = null)
    {
        var list = items.ToList();
        var result = new Dictionary<Guid, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dupNames = list.GroupBy(i => Safe(name(i)), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var baseTitle = Safe(dupNames.Contains(Safe(name(item))) && fallback is not null ? fallback(item) : name(item));
            var title = baseTitle;
            for (var n = 2; !taken.Add(title); n++) title = $"{baseTitle} ({n})";
            result[id(item)] = title;
        }
        return result;
    }

    /// <summary>File/wikilink-safe: Obsidian forbids * " \ / &lt; &gt; : | ? and # ^ [ ] in note names.</summary>
    public static string Safe(string s)
    {
        var cleaned = new string(s.Trim().Select(c => c is '*' or '"' or '\\' or '/' or '<' or '>' or ':' or '|' or '?' or '#' or '^' or '[' or ']' || char.IsControl(c) ? '-' : c).ToArray());
        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
        cleaned = cleaned.Trim('-', ' ', '.');
        return cleaned.Length == 0 ? "untitled" : cleaned;
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
    private static string Snake<T>(T e) where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(e.ToString());
    private static string Label<T>(T e) where T : struct, Enum => e.ToString();

    private static void CustomYaml(StringBuilder sb, JsonObject? props)
    {
        if (props is null) return;
        foreach (var (key, value) in props)
        {
            var k = Mapping.Slug(key);
            if (k is "id" or "type" or "area" or "status" or "created" or "updated") k += "_custom";
            Yaml(sb, k, value);
        }
    }

    private static void Yaml(StringBuilder sb, string key, object? value)
    {
        if (value is null) return;
        var rendered = value switch
        {
            string s => Quote(s),
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTime dt => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            JsonValue jv => YamlScalar(jv),
            JsonArray ja => "[" + string.Join(", ", ja.Select(n => n is JsonValue v ? YamlScalar(v) : Quote(n?.ToJsonString() ?? ""))) + "]",
            JsonObject jo => Quote(jo.ToJsonString()),
            IEnumerable<string> list => "[" + string.Join(", ", list.Select(Quote)) + "]",
            _ => Quote(value.ToString() ?? ""),
        };
        sb.Append(key).Append(": ").AppendLine(rendered);
    }

    private static string YamlScalar(JsonValue v)
    {
        if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (v.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetValue<string>(out var s)) return Quote(s);
        return Quote(v.ToJsonString());
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}
