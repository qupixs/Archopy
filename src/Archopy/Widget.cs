using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Archopy;

internal sealed class Widget : Window
{
    private readonly Border surface;
    private readonly Border grip;
    private Border? caption;
    private readonly Grid shell;
    private Thumb? resizeHandle;
    private Button? finishSizing;
    private bool plain;
    public event Action<int, int>? Resized;
    public event Action? SizeAccepted;
    private bool pinned;
    private bool snapping;
    private bool playerExpanded;
    private readonly Action<Position> rememberPosition;
    private IntPtr Handle => new WindowInteropHelper(this).Handle;
    public FrameworkElement Body { get; }
    public TextBlock Label => TextIn(Body, 0);
    public TextBlock Detail => TextIn(Body, 1);

    private readonly bool isClock;
    private readonly bool isPlayer;

    public Widget(string id, int width, FrameworkElement body, bool pinned, Action openSettings, Action<Position> moved)
    {
        this.pinned = pinned;
        rememberPosition = moved;
        Body = body;
        isClock = id == "desktop-time";
        isPlayer = id == "spotify";
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = pinned;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;

        var layout = new StackPanel();
        grip = new Border
        {
            Height = 17, Background = Brushes.Transparent, Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move. Right-click for settings.",
            Child = new Border
            {
                Width = 30, Height = 3, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromArgb(130, 140, 150, 166))
            }
        };
        var dragTarget = isClock ? body : grip;
        void Drag(object sender, MouseButtonEventArgs args)
        {
            args.Handled = true;
            try { DragMove(); }
            catch (InvalidOperationException) { return; }
            var rect = KeepOnScreen();
            moved(new Position(rect.Left, rect.Top));
            Lower();
        }
        dragTarget.MouseLeftButtonDown += Drag;
        if (isPlayer || id.StartsWith("photo-") || id == "lyrics") body.MouseLeftButtonDown += Drag;
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Archopy settings" };
        settings.Click += (_, _) => openSettings();
        menu.Items.Add(settings);
        dragTarget.ContextMenu = menu;
        body.ContextMenu = menu;
        if (!isClock) layout.Children.Add(grip);
        layout.Children.Add(body);
        surface = isClock || isPlayer ? new Border() : new CardSurface();
        surface.CornerRadius = new CornerRadius(24);
        surface.Padding = isClock ? new Thickness(0) : isPlayer ? new Thickness(26, 0, 26, 9) : new Thickness(15, 7, 15, 17);
        surface.Child = layout;
        shell = new Grid { Children = { surface } };
        if (!isPlayer && !isClock)
        {
            var caption = new TextBlock { Text = id.StartsWith("photo-") ? "PHOTO" : id.ToUpperInvariant(),
                FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            this.caption = new Border { Width = 76, Height = 19, CornerRadius = new CornerRadius(10),
                Background = Brushes.Black, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right,
                Child = caption, Padding = new Thickness(0, 3, 0, 0) };
            shell.Children.Add(this.caption);
        }
        Content = shell;
        Deactivated += (_, _) => Lower();
        SizeChanged += (_, _) =>
        {
            if (isPlayer) UpdatePlayerCorners();
            if (IsVisible && Handle != IntPtr.Zero) KeepOnScreen();
        };
    }

    public void Apply(Settings settings)
    {
        snapping = settings.SnapToScreen;
        var scale = Math.Clamp(settings.ClockScale, .4, 2.5);
        var vertical = scale * Math.Clamp(settings.ClockStretch, .5, 2.5);
        var clockWidth = (settings.HorizontalTime ? 380 : 300) * scale;
        if (isClock && (Width != clockWidth || Label.LayoutTransform is not ScaleTransform transform ||
            transform.ScaleX != scale || transform.ScaleY != vertical))
        {
            var oldBounds = new NativeRect();
            var align = snapping && IsVisible && GetWindowRect(Handle, out oldBounds);
            Width = clockWidth;
            Label.LayoutTransform = new ScaleTransform(scale, vertical);
            Label.Text = settings.HorizontalTime ? "00 00" : "00\n00";
            if (align) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => AlignResizedClock(oldBounds));
        }
        var ink = (isClock ? !settings.Dark : settings.Dark) ? Brushes.White : Brushes.Black;
        if (Foreground != ink) Foreground = ink;
        var style = (settings.Dark, settings.Surface, settings.Color, settings.GradientEnd, plain);
        if (Equals(surface.Tag, style)) return;
        surface.Tag = style;
        if (isPlayer)
        {
            Resources["PillInk"] = ink;
            Resources["PillFill"] = new SolidColorBrush(settings.Dark ? Color.FromArgb(52, 255, 255, 255) : Color.FromArgb(34, 0, 0, 0));
        }
        var start = ParseColor(settings.Color, Color.FromRgb(55, 71, 106));
        var end = ParseColor(settings.GradientEnd, Color.FromRgb(118, 83, 152));
        surface.Background = isClock || plain ? Brushes.Transparent : settings.Surface switch
        {
            "Transparent" => Brushes.Transparent,
            "Solid" => new SolidColorBrush(start),
            "Gradient" => new LinearGradientBrush(start, end, 35),
            _ when settings.Dark => new LinearGradientBrush(Color.FromArgb(222, 33, 33, 33), Color.FromArgb(205, 0, 0, 0), 90),
            _ => new LinearGradientBrush(Color.FromArgb(237, 255, 255, 255), Color.FromArgb(202, 245, 245, 245), 90)
        };
    }

    private void AlignResizedClock(NativeRect old)
    {
        if (!IsVisible || !GetWindowRect(Handle, out var rect)) return;
        var bounds = System.Drawing.Rectangle.FromLTRB(old.Left, old.Top, old.Right, old.Bottom);
        var area = Forms.Screen.FromRectangle(bounds).WorkingArea;
        static int Align(int start, int oldSize, int newSize, int origin, int span)
        {
            if (Math.Abs(start - (origin + (span - oldSize) / 2)) <= 2) return origin + (span - newSize) / 2;
            if (Math.Abs(start + oldSize - origin - span) <= 2) return origin + span - newSize;
            return start;
        }
        var left = Align(old.Left, bounds.Width, rect.Right - rect.Left, area.Left, area.Width);
        var top = Align(old.Top, bounds.Height, rect.Bottom - rect.Top, area.Top, area.Height);
        SetWindowPos(Handle, IntPtr.Zero, left, top, 0, 0, NoSize | NoActivate | NoZOrder);
        rect = KeepOnScreen();
        rememberPosition(new Position(rect.Left, rect.Top));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(Handle)?.AddHook(SnapWhileMoving);
    }

    private IntPtr SnapWhileMoving(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmMoving = 0x0216;
        if (!snapping || message != wmMoving) return IntPtr.Zero;
        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var area = Forms.Screen.FromRectangle(bounds).WorkingArea;
        var tolerance = (int)Math.Round(16 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        rect.Left = SnapAxis(bounds.Left, bounds.Width, area.Left, area.Width, tolerance);
        rect.Top = SnapAxis(bounds.Top, bounds.Height, area.Top, area.Height, tolerance);
        rect.Right = rect.Left + bounds.Width;
        rect.Bottom = rect.Top + bounds.Height;
        Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    internal static int SnapAxis(int position, int size, int origin, int span, int tolerance)
    {
        var travel = Math.Max(0, span - size);
        var nearest = origin;
        foreach (var candidate in new[] { origin + travel / 2, origin + travel })
            if (Math.Abs(candidate - position) < Math.Abs(nearest - position)) nearest = candidate;
        return Math.Abs(nearest - position) <= tolerance ? nearest : position;
    }

    public static Color ParseColor(string value, Color fallback)
    {
        if (value is { Length: 7 } && value[0] == '#' &&
            uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return fallback;
    }

    public void ShowAt(Position? position, string id, int photoIndex)
    {
        Opacity = 0;
        Show();
        UpdateLayout();
        GetWindowRect(Handle, out var rect);
        var area = Forms.Screen.PrimaryScreen!.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        int Offset(int value) => (int)Math.Round(value * dpi.DpiScaleX);
        var center = area.Left + (area.Width - (rect.Right - rect.Left)) / 2;
        position ??= id switch
        {
            "spotify" => new(center, area.Top + Offset(18)),
            "desktop-time" => new(center, area.Top + (area.Height - (rect.Bottom - rect.Top)) / 2),
            "date" => new(area.Left + Offset(28), area.Top + Offset(230)),
            "calendar" => new(area.Left + Offset(28), area.Top + Offset(322)),
            "lyrics" => new(center, area.Bottom - Offset(220)),
            _ => new(area.Right - Offset(274 + 22 * (photoIndex % 8)),
                area.Top + Offset(88 + 24 * (photoIndex % 8)))
        };
        SetWindowPos(Handle, IntPtr.Zero, position.X, position.Y, 0, 0, NoSize | NoActivate | NoZOrder);
        KeepOnScreen();
        Lower();
        Opacity = 1;
    }

    public void SetPinned(bool value)
    {
        if (pinned == value) return;
        pinned = value;
        Topmost = value;
        Lower();
    }

    private void Lower()
    {
        if (!pinned && Handle != IntPtr.Zero)
            SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, NoSize | NoMove | NoActivate);
    }

    private NativeRect KeepOnScreen()
    {
        GetWindowRect(Handle, out var rect);
        var bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var area = Forms.Screen.FromRectangle(bounds).WorkingArea;
        var left = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - bounds.Width));
        var top = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - bounds.Height));
        if (left != rect.Left || top != rect.Top)
        {
            SetWindowPos(Handle, IntPtr.Zero, left, top, 0, 0, NoSize | NoActivate | NoZOrder);
            GetWindowRect(Handle, out rect);
        }
        return rect;
    }

    private const uint NoSize = 0x0001, NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    private static TextBlock TextIn(FrameworkElement body, int index) =>
        (TextBlock)((StackPanel)body).Children[index];

    private static TextBlock Text(double size, bool strong = false) => new()
    {
        FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    public static FrameworkElement Clock()
    {
        var layout = new StackPanel { Cursor = Cursors.SizeAll, Background = Brushes.Transparent };
        var digits = Text(112, true);
        digits.FontWeight = FontWeights.Bold;
        digits.TextAlignment = TextAlignment.Center;
        digits.LineHeight = 99;
        digits.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        digits.Text = "00\n00";
        layout.Children.Add(digits);
        layout.Children.Add(new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, MinHeight = 16 });
        return layout;
    }

    public static FrameworkElement Date()
    {
        var text = Text(19, true);
        text.TextWrapping = TextWrapping.Wrap;
        return new StackPanel { Margin = new Thickness(7, 0, 7, 4), Children = { text } };
    }

    public static FrameworkElement Lyrics()
    {
        var line = Text(24, true);
        line.TextWrapping = TextWrapping.Wrap;
        line.TextAlignment = TextAlignment.Center;
        var title = Text(12);
        title.Opacity = 0.75;
        title.TextAlignment = TextAlignment.Center;
        var text = new StackPanel { Children = { line, title } };
        text.MaxHeight = 300;
        text.ClipToBounds = true;
        return text;
    }

    public static FrameworkElement Calendar(DateTime date)
    {
        var content = new StackPanel { Margin = new Thickness(5, 0, 5, 3) };
        FillCalendar(content, date);
        return content;
    }

    public static void FillCalendar(StackPanel layout, DateTime date)
    {
        layout.Tag = DateOnly.FromDateTime(date);
        layout.Children.Clear();
        var culture = CultureInfo.CurrentCulture;
        var title = Text(22, true);
        title.Text = date.ToString("Y", culture);
        title.Margin = new Thickness(4, 0, 0, 9);
        layout.Children.Add(title);
        var grid = new UniformGrid { Columns = 7 };
        layout.Children.Add(grid);
        var firstDay = (int)culture.DateTimeFormat.FirstDayOfWeek;
        for (var i = 0; i < 7; i++)
        {
            var label = Text(11);
            label.Text = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((i + firstDay) % 7));
            label.TextAlignment = TextAlignment.Center;
            label.Opacity = 0.74;
            grid.Children.Add(label);
        }
        var calendar = culture.DateTimeFormat.Calendar;
        var current = calendar.GetDayOfMonth(date);
        var start = date.AddDays(1 - current);
        var offset = ((int)start.DayOfWeek - firstDay + 7) % 7;
        for (var i = 0; i < offset; i++) grid.Children.Add(new Border());
        var days = calendar.GetDaysInMonth(calendar.GetYear(date), calendar.GetMonth(date), calendar.GetEra(date));
        for (var day = 1; day <= days; day++)
        {
            var number = Text(13, day == current);
            number.Text = day.ToString(culture);
            number.TextAlignment = TextAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(new Border
            {
                Height = 27, Margin = new Thickness(4, 2, 4, 0), CornerRadius = new CornerRadius(10),
                Background = day == current ? new SolidColorBrush(Color.FromArgb(95, 135, 135, 135)) : null,
                Child = number
            });
        }
    }

    public static FrameworkElement Photo(Photo photo)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(photo.Path);
            var image = new Border { CornerRadius = new CornerRadius(18), Background = new ImageBrush(Playback.Decode(stream)) };
            AutomationProperties.SetName(image, photo.Name);
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or FormatException)
        {
            return new TextBlock { Text = "Image unavailable", ToolTip = photo.Name };
        }
    }

    private void SetFrame(bool framed)
    {
        plain = !framed;
        grip.Visibility = framed ? Visibility.Visible : Visibility.Collapsed;
        if (caption != null) caption.Visibility = grip.Visibility;
        surface.Padding = framed ? new Thickness(15, 7, 15, 17) : new Thickness(0);
    }

    private void EnableResize(bool visible, int minWidth, int maxWidth, int minHeight, int maxHeight)
    {
        if (resizeHandle is null)
        {
            resizeHandle = new Thumb { Width = 20, Height = 20, Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = "Drag to resize", Style = (Style)FindResource("ResizeHandle") };
            resizeHandle.DragDelta += (_, e) =>
            {
                Width = Math.Clamp(Width + e.HorizontalChange, minWidth, maxWidth);
                Body.Height = Math.Clamp(Body.Height + e.VerticalChange, minHeight, maxHeight);
            };
            resizeHandle.DragCompleted += (_, _) => Resized?.Invoke((int)Math.Round(Width), (int)Math.Round(Body.Height));
            shell.Children.Add(resizeHandle);
        }
        resizeHandle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ConfigurePhoto(Photo photo, Settings settings)
    {
        SetFrame(photo.Framed);
        Width = Math.Clamp(photo.Width, 120, 1000);
        Body.Height = Math.Clamp(photo.Height, 80, 800);
        if (Body is Border { Background: ImageBrush brush } && brush.ImageSource is System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            var aspect = Math.Max(1, Width - surface.Padding.Left - surface.Padding.Right) / Body.Height;
            var ratio = aspect * bitmap.PixelHeight / bitmap.PixelWidth;
            var zoom = Math.Clamp(photo.Zoom, 1, 4);
            var w = Math.Min(1, ratio) / zoom;
            var h = Math.Min(1, 1 / ratio) / zoom;
            brush.Viewbox = new Rect((1 - w) * Math.Clamp(photo.FocusX, 0, 1),
                (1 - h) * Math.Clamp(photo.FocusY, 0, 1), w, h);
            brush.Stretch = Stretch.Fill;
        }
        EnableResize(photo.Framed, 120, 1000, 80, 800);
        Apply(settings);
    }

    public void ConfigureLyrics(Settings settings)
    {
        SetFrame(!settings.LyricsPlaced);
        // Preserve the user's live dimensions until the resize gesture commits.
        if (resizeHandle?.IsDragging != true)
        {
            Width = Math.Clamp(settings.LyricsWidth, 200, 1200);
            Body.Height = Math.Clamp(settings.LyricsHeight, 36, 600);
        }
        Body.MaxHeight = 600;
        Label.FontSize = 21;
        Label.LineHeight = 32;
        Label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        Label.TextWrapping = TextWrapping.Wrap;
        Label.MaxHeight = Math.Max(1, (int)(Body.Height / 32)) * 32;
        Detail.Visibility = Visibility.Collapsed;
        EnableResize(!settings.LyricsPlaced, 200, 1200, 36, 600);
        if (finishSizing is null)
        {
            finishSizing = new Button { Content = "Done", HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5), Padding = new Thickness(8, 2, 8, 2) };
            finishSizing.Click += (_, _) => SizeAccepted?.Invoke();
            shell.Children.Add(finishSizing);
        }
        finishSizing.Visibility = settings.LyricsPlaced ? Visibility.Collapsed : Visibility.Visible;
        Apply(settings);
        if (!settings.LyricsPlaced && settings.Surface == "Transparent")
            surface.Background = settings.Dark ? Brushes.Black : Brushes.White;
        Label.Foreground = settings.LyricsPlaced ? new SolidColorBrush(ParseColor(settings.LyricsColor, Colors.White)) : Foreground;
    }

    public static FrameworkElement Player(Action toggle, Func<MediaAction, TimeSpan, Task> control) =>
        new PlayerBody(toggle, control);

    public void UpdatePlayer(Track? track, bool expanded, bool connected = true)
    {
        grip.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        surface.Padding = expanded ? new Thickness(26, 0, 26, 9) : new Thickness(16, 7, 12, 7);
        if (playerExpanded != expanded)
        {
            playerExpanded = expanded;
            UpdatePlayerCorners();
        }
        ((PlayerBody)Body).Update(track, expanded, connected);
    }

    private void UpdatePlayerCorners()
    {
        var shortSide = Math.Min(ActualWidth, ActualHeight);
        surface.CornerRadius = new CornerRadius(playerExpanded ? Math.Min(22, shortSide / 4) : shortSide / 2);
    }

    private sealed class PlayerBody : StackPanel
    {
        private readonly TextBlock title = Text(14, true);
        private readonly TextBlock artist = Text(11);
        private readonly TextBlock album = Text(12);
        private readonly TextBlock attribution = Text(11);
        private Uri? spotifyUrl;
        private readonly TextBlock time = Text(11);
        private readonly Image cover = new() { Width = 38, Height = 38, Stretch = Stretch.Uniform };
        private readonly FeatherIcon note = new() { Glyph = "music" };
        private readonly StackPanel details = new() { Margin = new Thickness(5, 11, 5, 0) };
        private readonly Slider progress = new() { Minimum = 0, SmallChange = 5, LargeChange = 15 };
        private readonly Button play;
        private readonly Button expand;
        private readonly Button previous;
        private readonly Button next;
        private readonly Button quickPlay;
        private readonly StackPanel compactMeter = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border elapsedBar = new() { Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        private bool draggingProgress;
        private bool updatingProgress;

        public PlayerBody(Action toggle, Func<MediaAction, TimeSpan, Task> control)
        {
            var header = new DockPanel { MinHeight = 43, LastChildFill = true };
            expand = Button("down", "Expand Spotify");
            expand.Click += (_, _) => toggle();
            DockPanel.SetDock(expand, Dock.Right);
            header.Children.Add(expand);
            quickPlay = Button("play", "Play");
            quickPlay.Click += async (_, _) => await control(MediaAction.Toggle, TimeSpan.Zero);
            DockPanel.SetDock(quickPlay, Dock.Right);
            header.Children.Add(quickPlay);

            note.VerticalAlignment = VerticalAlignment.Center;
            note.HorizontalAlignment = HorizontalAlignment.Center;
            var artwork = new Grid { Width = 40, Height = 40, Margin = new Thickness(1, 0, 10, 0) };
            artwork.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(54, 128, 128, 128)), CornerRadius = new CornerRadius(10),
                Child = note
            });
            artwork.Children.Add(cover);
            DockPanel.SetDock(artwork, Dock.Left);
            header.Children.Add(artwork);
            var artistRow = new DockPanel();
            var rail = new Grid { Width = 39, Height = 12, Margin = new Thickness(4, 0, 3, 0) };
            rail.Children.Add(new Border { Height = 3, Background = Brushes.Gray, Opacity = .45, CornerRadius = new CornerRadius(2) });
            elapsedBar.SetResourceReference(Border.BackgroundProperty, "PillInk");
            rail.Children.Add(elapsedBar);
            compactMeter.Children.Add(rail);
            compactMeter.Children.Add(new FeatherIcon { Glyph = "activity", Width = 13, Height = 13 });
            DockPanel.SetDock(compactMeter, Dock.Right);
            artistRow.Children.Add(compactMeter);
            artistRow.Children.Add(artist);
            header.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, artistRow } });
            artist.Opacity = 0.78;
            Children.Add(header);

            details.Children.Add(album);
            var sourceLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Open track in Spotify"));
            sourceLink.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "PillInk");
            sourceLink.Click += (_, _) =>
            {
                if (spotifyUrl == null) return;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(spotifyUrl.AbsoluteUri) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { }
            };
            attribution.Inlines.Add(sourceLink);
            details.Children.Add(attribution);
            album.Opacity = 0.78;
            AutomationProperties.SetName(progress, "Playback position");
            progress.IsMoveToPointEnabled = true;
            progress.PreviewMouseLeftButtonDown += (_, _) => draggingProgress = true;
            progress.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(async (_, args) =>
            {
                if (!draggingProgress || args.ChangedButton != MouseButton.Left) return;
                draggingProgress = false;
                await control(MediaAction.Seek, TimeSpan.FromSeconds(progress.Value));
            }), true);
            progress.LostMouseCapture += (_, _) => draggingProgress = false;
            progress.ValueChanged += async (_, _) =>
            {
                if (!updatingProgress && !draggingProgress && progress.IsKeyboardFocusWithin)
                    await control(MediaAction.Seek, TimeSpan.FromSeconds(progress.Value));
            };
            details.Children.Add(progress);
            details.Children.Add(time);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            previous = Button("previous", "Previous track");
            play = Button("play", "Play");
            next = Button("next", "Next track");
            previous.Click += async (_, _) => await control(MediaAction.Previous, TimeSpan.Zero);
            play.Click += async (_, _) => await control(MediaAction.Toggle, TimeSpan.Zero);
            next.Click += async (_, _) => await control(MediaAction.Next, TimeSpan.Zero);
            buttons.Children.Add(previous);
            buttons.Children.Add(play);
            buttons.Children.Add(next);
            details.Children.Add(buttons);
            Children.Add(details);
        }

        public void Update(Track? track, bool expanded, bool connected)
        {
            var canResume = track is { Playing: false };
            if (canResume) track = null;
            title.Text = connected ? track?.Title ?? "Nothing playing" : "Log in with Spotify";
            artist.Text = track?.Artist is { Length: > 0 } name ? name : "Spotify";
            cover.Source = track?.Art;
            note.Visibility = cover.Source is null ? Visibility.Visible : Visibility.Hidden;
            details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ((FeatherIcon)expand.Content).Glyph = expanded ? "up" : "down";
            expand.ToolTip = expanded ? "Collapse Spotify" : "Expand Spotify";
            AutomationProperties.SetName(expand, (string)expand.ToolTip);
            album.Text = track?.Album ?? "";
            spotifyUrl = track?.SpotifyUrl;
            attribution.Visibility = spotifyUrl == null ? Visibility.Collapsed : Visibility.Visible;
            album.Visibility = album.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            var elapsed = Math.Max(0, track?.CurrentPosition.TotalSeconds ?? 0);
            var length = Math.Max(0, track?.Duration.TotalSeconds ?? 0);
            progress.IsEnabled = length > 0;
            if (!draggingProgress)
            {
                updatingProgress = true;
                progress.Maximum = Math.Max(1, length);
                progress.Value = Math.Min(elapsed, length);
                updatingProgress = false;
            }
            time.Text = length > 0 ? $"{Format(elapsed)}  /  {Format(length)}" : "";
            ((FeatherIcon)play.Content).Glyph = track?.Playing == true ? "pause" : "play";
            ((FeatherIcon)quickPlay.Content).Glyph = !connected ? "login" : track?.Playing == true ? "pause" : "play";
            quickPlay.ToolTip = !connected ? "Log in with Spotify" : track?.Playing == true ? "Pause" : "Play";
            AutomationProperties.SetName(quickPlay, (string)quickPlay.ToolTip);
            quickPlay.Visibility = expanded && connected ? Visibility.Collapsed : Visibility.Visible;
            quickPlay.IsEnabled = !connected || track is not null || canResume;
            compactMeter.Visibility = !expanded && length > 0 ? Visibility.Visible : Visibility.Collapsed;
            elapsedBar.Width = length > 0 ? 39 * Math.Clamp(elapsed / length, 0, 1) : 0;
            AutomationProperties.SetName(play, track?.Playing == true ? "Pause" : "Play");
            previous.IsEnabled = play.IsEnabled = next.IsEnabled = track is not null;
            play.IsEnabled = track is not null || canResume;
            progress.Visibility = time.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string Format(double seconds) => TimeSpan.FromSeconds(seconds).ToString(
            seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

        private static Button Button(string text, string name)
        {
            var button = new Button
            {
                Content = new FeatherIcon { Glyph = text }, Width = 34, Height = 34, Margin = new Thickness(4, 1, 4, 1),
                Padding = new Thickness(0), ToolTip = name
            };
            button.SetBinding(ForegroundProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Widget), 1)
            });
            AutomationProperties.SetName(button, name);
            return button;
        }
    }
}
