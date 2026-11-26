using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Archopy;

public partial class App : Application
{
    private const string ClockId = "desktop-time";
    private const string DateId = "date";
    private const string CalendarId = "calendar";
    private const string SpotifyId = "spotify";
    private const string LyricsId = "lyrics";
    private readonly Dictionary<string, Widget> widgets = new();
    private readonly Playback playback = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Settings settings = new();
    private Forms.NotifyIcon? tray;
    private DesktopSettings? panel;
    private Mutex? instance;
    private bool ownsInstance;
    private bool polling;
    private bool exiting;
    private bool expanded;
    private Track? track;
    private string? loadedLyrics;
    private Lyrics? lyrics;
    private CancellationTokenSource? lyricRequest;
    private DateTimeOffset retryLyrics;
    private DateOnly? calendarDay;
    private bool smoke;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        smoke = e.Args.Contains("--smoke-test");
        instance = new Mutex(true, smoke ? @"Local\Archopy.Smoke" : @"Local\Archopy.Desktop", out ownsInstance);
        if (!ownsInstance)
        {
            if (!smoke)
                MessageBox.Show("Archopy is already running. Open it from the notification area.", "Archopy");
            Shutdown(1);
            return;
        }
        settings = smoke ? new Settings { Calendar = true, Date = true } : Settings.Load();
        if (!smoke) playback.Spotify.Restore();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true, Text = "Archopy" };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => Dispatcher.Invoke(OpenPanel));
        menu.Items.Add("Exit Archopy", null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(OpenPanel);
        };
        RefreshWidgets();
        if (smoke || (!e.Args.Contains("--startup") && !File.Exists(Path.Combine(Settings.DirectoryPath, "settings.json")))) OpenPanel();
        timer.Tick += Tick;
        timer.Start();
        if (smoke)
        {
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            stop.Tick += (_, _) => { stop.Stop(); Shutdown(); };
            stop.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        lyricRequest?.Cancel();
        timer.Stop();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }

    private void OpenPanel()
    {
        if (panel is null)
        {
            panel = new DesktopSettings(settings, playback.Spotify, SaveAndRefresh, ImportLyrics);
            panel.PlaceDefaultsRequested += () =>
            {
                foreach (var key in widgets.Keys.ToArray()) Remove(key);
            };
            panel.Closed += (_, _) => panel = null;
        }
        if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal;
        if (!panel.IsVisible) panel.Show();
        panel.Activate();
    }

    private void SaveAndRefresh()
    {
        Save();
        RefreshWidgets();
    }

    private void Save()
    {
        if (smoke) return;
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tray?.ShowBalloonTip(4000, "Archopy", "Windows could not save your desktop settings.", Forms.ToolTipIcon.Error);
        }
    }

    private string ImportLyrics()
    {
        if (track is null) return "Start a Spotify track before importing lyrics.";
        var key = track.Key;
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Timed or plain lyrics|*.lrc;*.txt" };
        if (picker.ShowDialog(panel) != true) return "";
        try
        {
            if (new FileInfo(picker.FileName).Length > 256_000) return "Choose a lyrics file under 256 KB.";
            var file = Settings.Import(picker.FileName);
            settings.LyricFiles[key] = file;
            settings.Lyrics = true;
            SaveAndRefresh();
            return "Lyrics linked to the current track.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Windows could not import the lyrics file.";
        }
    }

    private DateTime Now => settings.Utc ? DateTime.UtcNow : DateTime.Now;

    private void RefreshWidgets()
    {
        var now = Now;
        Ensure(ClockId, settings.Clock, 300, settings, () => Widget.Clock());
        Ensure(DateId, settings.Date, 230, settings, () => Widget.Date());
        Ensure(CalendarId, settings.Calendar, 326, settings, () => Widget.Calendar(now));
        Ensure(SpotifyId, settings.Spotify, 336, settings, () => Widget.Player(
            () => { expanded = !expanded; UpdatePlayer(); },
            async (action, position) => await ControlAsync(action, position)));
        for (var index = 0; index < settings.Photos.Count; index++)
        {
            var photo = settings.Photos[index];
            var creating = !widgets.ContainsKey(photo.Id);
            Ensure(photo.Id, true, 245, settings, () => Widget.Photo(photo), index);
            var widget = widgets[photo.Id];
            if (creating) widget.Resized += (width, height) =>
            {
                photo.Width = width; photo.Height = height;
                widget.ConfigurePhoto(photo, settings);
                Save();
            };
            widget.ConfigurePhoto(photo, settings);
        }
        var photoIds = settings.Photos.Select(photo => photo.Id).ToHashSet();
        foreach (var key in widgets.Keys.Where(key => key.StartsWith("photo-") && !photoIds.Contains(key)).ToArray())
            Remove(key);
        if (widgets.TryGetValue(SpotifyId, out var player)) player.SetPinned(settings.Pinned);
        UpdateClock(now);
        UpdatePlayer();
        UpdateLyrics(true);
    }

    private void Ensure(string id, bool enabled, int width, Settings appearance, Func<FrameworkElement> content,
        int photoIndex = 0)
    {
        if (!enabled) { Remove(id); return; }
        if (!widgets.TryGetValue(id, out var widget))
        {
            widget = new Widget(id, width, content(), id == SpotifyId && settings.Pinned, OpenPanel, position =>
            {
                settings.Positions[id] = position;
                Save();
            });
            widgets.Add(id, widget);
            widget.Apply(appearance);
            widget.ShowAt(settings.Positions.TryGetValue(id, out var position) ? position : null, id, photoIndex);
        }
        else widget.Apply(appearance);
    }

    private void Remove(string id)
    {
        if (widgets.Remove(id, out var widget)) widget.Close();
    }

    private void UpdateClock(DateTime now)
    {
        if (widgets.TryGetValue(ClockId, out var clock))
        {
            var culture = CultureInfo.CurrentCulture;
            var twelveHour = culture.DateTimeFormat.ShortTimePattern.Contains('h');
            clock.Label.Text = now.ToString(twelveHour ? "hh" : "HH", culture)
                + (settings.HorizontalTime ? " " : "\n") + now.ToString("mm", culture);
            clock.Detail.Text = string.Join(" · ", new[] { twelveHour ? now.ToString("tt", culture) : "",
                settings.Utc ? "UTC" : "" }.Where(part => part.Length > 0));
        }
        if (widgets.TryGetValue(DateId, out var date)) date.Label.Text = now.ToString("D", CultureInfo.CurrentCulture);
        var day = DateOnly.FromDateTime(now);
        if (widgets.TryGetValue(CalendarId, out var calendar)
            && (calendarDay != day || calendar.Body.Tag is not DateOnly))
        {
            Widget.FillCalendar((StackPanel)calendar.Body, now);
            calendarDay = day;
        }
    }

    private async void Tick(object? sender, EventArgs args)
    {
        UpdateClock(Now);
        UpdatePlayer();
        UpdateLyrics();
        if (polling || (!settings.Spotify && !settings.Lyrics && panel is null)) return;
        polling = true;
        try
        {
            track = await playback.ReadAsync();
            if (exiting) return;
            UpdatePlayer();
            UpdateLyrics();
        }
        finally { polling = false; }
    }

    private void UpdatePlayer()
    {
        if (widgets.TryGetValue(SpotifyId, out var player))
            player.UpdatePlayer(playback.Spotify.Connected ? track : null, expanded, playback.Spotify.Connected);
    }

    private async System.Threading.Tasks.Task ControlAsync(MediaAction action, TimeSpan position)
    {
        if (!playback.Spotify.Connected)
        {
            OpenPanel();
            panel!.LoginToSpotify();
            return;
        }
        if (track is null) return;
        try
        {
            if (await track.Control(action, position)) return;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException
            or System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        tray?.ShowBalloonTip(3000, "Archopy", "This Spotify device could not perform that action.", Forms.ToolTipIcon.Info);
    }

    private void UpdateLyrics(bool refresh = false)
    {
        var song = track is { Playing: true } && playback.Spotify.Connected ? track : null;
        string? file = null;
        if (song != null) settings.LyricFiles.TryGetValue(song.Key, out file);
        var key = settings.Lyrics && song != null ? song.Key + file : null;
        if (loadedLyrics != key || (retryLyrics != default && DateTimeOffset.UtcNow >= retryLyrics))
        {
            lyricRequest?.Cancel();
            lyrics = null;
            loadedLyrics = key;
            retryLyrics = default;
            if (key != null && song != null) _ = LoadLyricsAsync(song, file);
        }
        if (!settings.Lyrics || (settings.LyricsPlaced && (lyrics == null || song == null)))
        {
            Remove(LyricsId);
            return;
        }
        bool creating = !widgets.ContainsKey(LyricsId);
        if (creating) Ensure(LyricsId, true, settings.LyricsWidth, settings, () => Widget.Lyrics());
        var widget = widgets[LyricsId];
        if (creating)
        {
            widget.Resized += (width, height) =>
            {
                settings.LyricsWidth = width;
                settings.LyricsHeight = height;
                settings.LyricsPlaced = true;
                SaveAndRefresh();
            };
            widget.SizeAccepted += () => { settings.LyricsPlaced = true; SaveAndRefresh(); };
        }
        if (refresh || creating) widget.ConfigureLyrics(settings);
        var capacity = Math.Max(1, (int)(widget.Body.Height / 32));
        var text = song != null ? lyrics?.At(song.CurrentPosition, capacity) : null;
        widget.Label.Text = !settings.LyricsPlaced && string.IsNullOrWhiteSpace(text)
            ? "Drag the corner to size lyrics" : text ?? "";
    }

    private async System.Threading.Tasks.Task LoadLyricsAsync(Track song, string? file)
    {
        using var request = new CancellationTokenSource();
        lyricRequest = request;
        try
        {
            var result = file != null && File.Exists(file) ? Lyrics.Read(file) : await Lyrics.LookupAsync(song, request.Token);
            if (!request.IsCancellationRequested) lyrics = result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException
            or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            if (!request.IsCancellationRequested) retryLyrics = DateTimeOffset.UtcNow.AddMinutes(1);
        }
        finally { if (lyricRequest == request) lyricRequest = null; }
    }
}
