namespace Loupedeck.VoxRingPlugin.Destinations;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Loupedeck.VoxRingPlugin.Models;

public class CalendarDestination : IDestination
{
    public string Name => "Calendar";
    public string Description => "Create a calendar event via the default calendar app";
    public bool IsAvailable => true;
    public DestinationCategory Category => DestinationCategory.Ai;
    public string AiPrompt =>
        "Convert this speech into a calendar event. " +
        "Output ONLY a JSON object with these fields: " +
        "title (string), start (ISO 8601 local datetime, e.g. 2026-04-22T14:00:00), " +
        "end (ISO 8601 local datetime), location (string, empty if not mentioned), " +
        "description (one-sentence summary). No markdown, no explanation, JSON only.";

    public async Task<bool> SendAsync(string text)
    {
        try
        {
            var ics = BuildIcs(text);
            var tempFile = Path.Combine(Path.GetTempPath(), $"voxring-{Guid.NewGuid():N}.ics");
            await File.WriteAllTextAsync(tempFile, ics, Encoding.UTF8);

            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });
            PluginLog.Info($"Calendar: opened ICS from '{(text.Length > 60 ? text[..60] : text)}'");
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Calendar: failed: {ex.Message}");
            return false;
        }
    }

    private static string BuildIcs(string text)
    {
        string title = "New Event";
        DateTime start = DateTime.Now.AddHours(1);
        DateTime end   = start.AddHours(1);
        string location = "";
        string description = "";

        // Try to extract JSON from the AI response
        try
        {
            var jStart = text.IndexOf('{');
            var jEnd   = text.LastIndexOf('}');
            if (jStart >= 0 && jEnd > jStart)
            {
                using var doc = JsonDocument.Parse(text[jStart..(jEnd + 1)]);
                var r = doc.RootElement;

                if (r.TryGetProperty("title", out var t) && t.GetString() is { } ts && ts.Length > 0)
                    title = ts;
                if (r.TryGetProperty("start", out var s) && DateTime.TryParse(s.GetString(), out var sd))
                    start = sd;
                if (r.TryGetProperty("end", out var e) && DateTime.TryParse(e.GetString(), out var ed))
                    end = ed;
                if (r.TryGetProperty("location", out var l))
                    location = l.GetString() ?? "";
                if (r.TryGetProperty("description", out var d))
                    description = d.GetString() ?? "";
            }
        }
        catch
        {
            // JSON parse failed — use raw text as description, keep defaults
            description = text;
        }

        if (end <= start) end = start.AddHours(1);

        var uid  = Guid.NewGuid().ToString("N");
        var now  = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var sb   = new StringBuilder();

        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//VoxRing//VoxRing//EN");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}@voxring");
        sb.AppendLine($"DTSTAMP:{now}");
        sb.AppendLine($"DTSTART:{start:yyyyMMddTHHmmss}");
        sb.AppendLine($"DTEND:{end:yyyyMMddTHHmmss}");
        sb.AppendLine($"SUMMARY:{Esc(title)}");
        if (!string.IsNullOrWhiteSpace(location))
            sb.AppendLine($"LOCATION:{Esc(location)}");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"DESCRIPTION:{Esc(description)}");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        return sb.ToString();
    }

    private static string Esc(string v) =>
        v.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
         .Replace("\r", "").Replace("\n", "\\n");
}
