using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Archopy;

internal sealed class Lyrics
{
    private static readonly Regex Timestamp = new(@"\[(\d{1,5}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    private readonly string plain;
    private readonly (double Seconds, string Text)[] timed;

    private Lyrics(string plain, (double Seconds, string Text)[] timed)
    {
        this.plain = plain;
        this.timed = timed;
    }

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(18), MaxResponseContentBufferSize = 512_000 };

    public static async Task<Lyrics?> LookupAsync(Track song, CancellationToken cancellation)
    {
        var query = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(song.Title)
            + "&artist_name=" + Uri.EscapeDataString(song.Artist) + "&album_name=" + Uri.EscapeDataString(song.Album)
            + "&duration=" + Math.Round(song.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        request.Headers.UserAgent.ParseAdd("Archopy/1.0");
        using var response = await Client.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        if (!json.RootElement.TryGetProperty("syncedLyrics", out var field) || field.ValueKind != JsonValueKind.String)
            return null;
        var result = Parse((field.GetString() ?? "").Replace("\r", "").Split('\n'));
        return result.timed.Length > 0 ? result : null;
    }

    public static Lyrics Read(string file) => Parse(File.ReadLines(file));

    private static Lyrics Parse(IEnumerable<string> lines)
    {
        var plain = new List<string>();
        var timed = new List<(double Seconds, string Text)>();
        var offset = 0.0;
        foreach (var line in lines)
        {
            var matches = Timestamp.Matches(line);
            if (matches.Count == 0)
            {
                if (line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']')
                    && int.TryParse(line.AsSpan(8, line.Length - 9), CultureInfo.InvariantCulture, out var milliseconds))
                    offset = milliseconds / 1000.0;
                else if (!line.StartsWith('[')) plain.Add(line);
                continue;
            }
            var text = Timestamp.Replace(line, "").Trim();
            foreach (Match match in matches)
            {
                var fraction = match.Groups[3].Value.PadRight(3, '0');
                var seconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                    + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                    + int.Parse(fraction, CultureInfo.InvariantCulture) / 1000.0;
                timed.Add((seconds, text));
            }
        }
        return new(string.Join(Environment.NewLine, plain).Trim(),
            timed.OrderBy(line => line.Seconds).Select(line => (line.Seconds - offset, line.Text)).ToArray());
    }

    public string At(TimeSpan position, int lines = 1)
    {
        if (timed.Length == 0) return string.Join("\n", plain.Split('\n').Take(lines));
        for (var index = timed.Length - 1; index >= 0; index--)
        {
            if (timed[index].Seconds <= position.TotalSeconds)
                return string.Join("\n", timed.Skip(index).Take(lines).Select(line => line.Text));
        }
        return "";
    }
}
