using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;


namespace Archopy;

internal enum MediaAction { Toggle, Previous, Next, Seek }

internal sealed record Track(string Title, string Artist, string Album, bool Playing,
    TimeSpan Position, TimeSpan Duration, DateTimeOffset SampledAt, BitmapSource? Art,
    Func<MediaAction, TimeSpan, Task<bool>> Control)
{
    public Uri? SpotifyUrl { get; init; }
    public string Key => JsonSerializer.Serialize(new[] { Title, Artist, Album });
    public TimeSpan CurrentPosition
    {
        get
        {
            var position = Position + (Playing ? DateTimeOffset.UtcNow - SampledAt : TimeSpan.Zero);
            return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
        }
    }
}

internal sealed class Playback
{
    public SpotifyWeb Spotify { get; } = new();
    public Task<Track?> ReadAsync() => Spotify.ReadAsync();

    public static BitmapSource Decode(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 600;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

internal sealed class SpotifyWeb
{
    public const string RedirectUri = "http://127.0.0.1:43921/callback";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string ClientId = "32af7ae309f1446799a0e63e179a0b68";
    private static string TokenPath => Path.Combine(Settings.DirectoryPath, "spotify.session");
    private readonly SemaphoreSlim refreshing = new(1);
    private string accessToken = "";
    private string refreshToken = "";
    private DateTimeOffset expires;
    private DateTimeOffset nextPoll;
    private Track? current;
    private int generation;
    public bool Connected => accessToken.Length > 0 || refreshToken.Length > 0;
    public bool Remembered { get; private set; }

    public void Restore()
    {
        try
        {
            if (File.Exists(TokenPath)) refreshToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser));
            Remembered = refreshToken.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { }
    }

    public void Disconnect()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
        Remembered = false;
        generation++;
        accessToken = refreshToken = "";
        current = null;
        nextPoll = default;
    }

    public async Task ConnectAsync()
    {
        var state = RandomString();
        var verifier = RandomString();
        var challenge = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var listener = new TcpListener(IPAddress.Loopback, 43921);
        listener.Start();
        try
        {
            const string scopes = "user-read-playback-state user-modify-playback-state";
            var address = "https://accounts.spotify.com/authorize?response_type=code"
                + "&client_id=" + ClientId
                + "&scope=" + Uri.EscapeDataString(scopes)
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&state=" + state + "&code_challenge_method=S256&code_challenge=" + challenge;
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
            while (true)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = connection.GetStream();
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = await ReadRequestLineAsync(stream, requestTimeout.Token);
                var pieces = request.Split(' ');
                var callback = pieces.Length == 3 && pieces[0] == "GET"
                    && Uri.TryCreate("http://127.0.0.1:43921" + pieces[1], UriKind.Absolute, out var uri)
                    && uri.AbsolutePath == "/callback" ? Query(uri.Query) : new();
                var valid = callback.TryGetValue("state", out var received) && received == state;
                var accepted = valid && callback.ContainsKey("code");
                var body = accepted ? "Spotify authorization received. Return to Archopy."
                    : valid ? "Spotify authorization was declined. Return to Archopy." : "Invalid callback.";
                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, requestTimeout.Token);
                if (!valid) continue;
                if (!accepted) throw new InvalidOperationException("Spotify authorization was declined.");
                using var token = await RequestTokenAsync(new()
                {
                    ["client_id"] = ClientId, ["grant_type"] = "authorization_code",
                    ["code"] = callback["code"], ["redirect_uri"] = RedirectUri,
                    ["code_verifier"] = verifier
                }, timeout.Token);
                Disconnect();
                SetToken(token.RootElement);
                return;
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task<string> ReadRequestLineAsync(Stream stream, CancellationToken cancellation)
    {
        var data = new byte[8192];
        var length = 0;
        while (length < data.Length)
        {
            var count = await stream.ReadAsync(data.AsMemory(length), cancellation);
            if (count == 0) break;
            length += count;
            var end = Array.IndexOf(data, (byte)'\n', 0, length);
            if (end >= 0) return Encoding.ASCII.GetString(data, 0, end).TrimEnd('\r');
        }
        return "";
    }

    private static Dictionary<string, string> Query(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2) result[WebUtility.UrlDecode(parts[0])] = WebUtility.UrlDecode(parts[1]);
        }
        return result;
    }

    private static string RandomString() => Encode(RandomNumberGenerator.GetBytes(32));
    private static string Encode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<JsonDocument> RequestTokenAsync(Dictionary<string, string> form, CancellationToken cancellation = default)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await Http.PostAsync("https://accounts.spotify.com/api/token", content, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    private void SetToken(JsonElement token)
    {
        accessToken = token.GetProperty("access_token").GetString() ?? "";
        if (token.TryGetProperty("refresh_token", out var refresh))
        {
            refreshToken = refresh.GetString() ?? "";
            try
            {
                Directory.CreateDirectory(Settings.DirectoryPath);
                File.WriteAllBytes(TokenPath + ".tmp", ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken),
                    null, DataProtectionScope.CurrentUser));
                File.Move(TokenPath + ".tmp", TokenPath, true);
                Remembered = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            { Remembered = false; }
        }
        expires = DateTimeOffset.UtcNow.AddSeconds(token.GetProperty("expires_in").GetInt32() - 30);
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string path)
    {
        var session = generation;
        if (!Connected) return null;
        if (DateTimeOffset.UtcNow >= expires)
        {
            await refreshing.WaitAsync();
            try
            {
                if (session != generation) return null;
                if (DateTimeOffset.UtcNow >= expires)
                {
                    using var token = await RequestTokenAsync(new()
                    {
                        ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = ClientId
                    });
                    if (session != generation) return null;
                    SetToken(token.RootElement);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                if (session == generation) Disconnect();
                return null;
            }
            finally { refreshing.Release(); }
        }
        using var request = new HttpRequestMessage(method, "https://api.spotify.com/v1/me/player" + path);
        request.Headers.Authorization = new("Bearer", accessToken);
        return await Http.SendAsync(request);
    }

    public async Task<Track?> ReadAsync()
    {
        if (!Connected) return null;
        if (DateTimeOffset.UtcNow < nextPoll) return current;
        var session = generation;
        nextPoll = DateTimeOffset.UtcNow.AddSeconds(5);
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "");
            if (response is null || session != generation) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                nextPoll = response.Headers.RetryAfter?.Date ??
                    DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
            if (response.StatusCode == HttpStatusCode.Unauthorized) expires = default;
            if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
                return current = null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
                return current = null;
            var title = String(item, "name");
            var album = item.TryGetProperty("album", out var release) && release.ValueKind == JsonValueKind.Object
                ? String(release, "name") : "";
            var artist = item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
                ? string.Join(", ", artists.EnumerateArray().Select(entry => String(entry, "name"))) : "";
            var playing = root.TryGetProperty("is_playing", out var active) && active.ValueKind == JsonValueKind.True;
            var sampled = DateTimeOffset.UtcNow;
            BitmapSource? art = null;
            if (current?.Title == title && current.Artist == artist && current.Album == album)
                art = current.Art;
            else if (release.ValueKind == JsonValueKind.Object && release.TryGetProperty("images", out var images)
                && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
            {
                var address = String(images[0], "url");
                if (Uri.TryCreate(address, UriKind.Absolute, out var imageUri) && imageUri.Scheme == "https"
                    && imageUri.Host == "i.scdn.co")
                {
                    try
                    {
                        using var image = await Http.GetStreamAsync(imageUri);
                        art = Playback.Decode(image);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or FormatException or TaskCanceledException) { }
                }
            }
            if (session != generation) return null;
            return current = new Track(title, artist, album, playing, Milliseconds(root, "progress_ms"),
                Milliseconds(item, "duration_ms"), sampled, art, async (action, position) =>
                {
                    var (method, path) = action switch
                    {
                        MediaAction.Previous => (HttpMethod.Post, "/previous"),
                        MediaAction.Next => (HttpMethod.Post, "/next"),
                        MediaAction.Seek => (HttpMethod.Put, $"/seek?position_ms={(long)position.TotalMilliseconds}"),
                        _ => (HttpMethod.Put, playing ? "/pause" : "/play")
                    };
                    using var control = await SendAsync(method, path);
                    nextPoll = default;
                    return control?.IsSuccessStatusCode == true;
                })
            {
                SpotifyUrl = item.TryGetProperty("external_urls", out var links) && links.ValueKind == JsonValueKind.Object
                    && Uri.TryCreate(String(links, "spotify"), UriKind.Absolute, out var link)
                    && link.Scheme == Uri.UriSchemeHttps && link.Host == "open.spotify.com" ? link : null
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return current = null;
        }
    }

    private static string String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static TimeSpan Milliseconds(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;
}
