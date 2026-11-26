using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Archopy;

internal sealed class DesktopSettings : Window
{
    private readonly SolidColorBrush Ink = new();
    private readonly SolidColorBrush Muted = new();
    private readonly SolidColorBrush Tile = new();
    private readonly Settings settings;
    private readonly SpotifyWeb spotify;
    private readonly Action changed;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel photoList = new();
    private readonly CheckBox lyrics;
    private readonly Button connect;
    public void LoginToSpotify()
    {
        if (!spotify.Connected && connect.IsEnabled) connect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    }

    public DesktopSettings(Settings settings, SpotifyWeb spotify, Action changed, Func<string> importLyrics)
    {
        this.settings = settings;
        this.spotify = spotify;
        this.changed = changed;
        Title = "Archopy";
        ShowInTaskbar = false;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Width = 660;
        Height = 754;
        MinWidth = 450;
        MinHeight = 555;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ApplyPalette();
        status.Foreground = Muted;
        Foreground = Ink;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;

        var content = new StackPanel { Margin = new Thickness(31, 22, 31, 30) };
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
        var logo = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Archopy.png"));
        Icon = logo;
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(new Image { Source = logo, Width = 45, Height = 45, Margin = new Thickness(0, 0, 10, 0) });
        heading.Children.Add(new TextBlock { Text = "Archopy", FontSize = 34, FontWeight = FontWeights.SemiBold });
        content.Children.Add(heading);
        content.Children.Add(new TextBlock
        {
            Text = "Drag the desktop time or a widget handle to change its position.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 2, 0, 16)
        });

        var appearance = Section(content, "Appearance");
        appearance.Children.Add(Row("Minimalist", Choices(new[] { "Light", "Dark" }, settings.Dark ? "Dark" : "Light",
            value => { settings.Dark = value == "Dark"; ApplyPalette(); changed(); })));

        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        var start = ColorButton("Primary color", settings.Color, value => settings.Color = value);
        var end = ColorButton("Gradient end color", settings.GradientEnd, value => settings.GradientEnd = value);
        colors.Children.Add(start);
        colors.Children.Add(end);
        var colorRow = Row("Colors", colors);
        void UpdateColors()
        {
            colorRow.Visibility = settings.Surface is "Solid" or "Gradient" ? Visibility.Visible : Visibility.Collapsed;
            end.Visibility = settings.Surface == "Gradient" ? Visibility.Visible : Visibility.Collapsed;
        }
        var surface = Choices(new[] { "Glass", "Transparent", "Gradient", "Solid" }, settings.Surface, value =>
        {
            settings.Surface = value;
            UpdateColors();
            changed();
        });
        UpdateColors();
        appearance.Children.Add(Row("Surface", surface));
        appearance.Children.Add(colorRow);

        var widgets = Section(content, "Desktop widgets");
        var toggles = new WrapPanel();
        appearance.Children.Add(Toggle("Desktop time", settings.Clock, value => settings.Clock = value));
        appearance.Children.Add(Adjustment("Time size", .4, 2.5, .05, settings.ClockScale, value => settings.ClockScale = value));
        appearance.Children.Add(Adjustment("Vertical stretch", .5, 2.5, .05, settings.ClockStretch, value => settings.ClockStretch = value));
        const string startupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using (var startup = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(startupKey))
            appearance.Children.Add(Toggle("Open at Windows sign-in", startup?.GetValue("Archopy") is string, enabled =>
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(startupKey);
                if (enabled) key.SetValue("Archopy", "\"" + Environment.ProcessPath + "\" --startup");
                else key.DeleteValue("Archopy", false);
            }));
        appearance.Children.Add(Row("Time layout", Choices(new[] { "Stacked", "Horizontal" },
            settings.HorizontalTime ? "Horizontal" : "Stacked", value => { settings.HorizontalTime = value == "Horizontal"; changed(); })));
        widgets.Children.Add(Toggle("Snap to screen edges and center", settings.SnapToScreen, value => settings.SnapToScreen = value));
        toggles.Children.Add(Toggle("Date", settings.Date, value => settings.Date = value));
        toggles.Children.Add(Toggle("Calendar", settings.Calendar, value => settings.Calendar = value));
        widgets.Children.Add(toggles);
        appearance.Children.Add(Row("Time zone", Choices(new[] { "Local", "UTC" }, settings.Utc ? "UTC" : "Local",
            value => { settings.Utc = value == "UTC"; changed(); })));
        var addPhoto = Button("Add image");
        addPhoto.Click += (_, _) => ImportPhoto();
        var photoActions = new WrapPanel();
        photoActions.Children.Add(addPhoto);
        var reset = Button("Reset widget placement");
        reset.Click += (_, _) =>
        {
            settings.Positions.Clear();
            PlaceDefaultsRequested?.Invoke();
            changed();
        };
        photoActions.Children.Add(reset);
        widgets.Children.Add(photoActions);
        widgets.Children.Add(photoList);
        RefreshPhotos();

        var listening = Section(content, "Spotify");
        var listeningToggles = new WrapPanel();
        listeningToggles.Children.Add(Toggle("Show player", settings.Spotify, value => settings.Spotify = value));
        listeningToggles.Children.Add(Toggle("Always on top", settings.Pinned, value => settings.Pinned = value));
        lyrics = Toggle("Desktop lyrics", settings.Lyrics, value => settings.Lyrics = value);
        listeningToggles.Children.Add(lyrics);
        listening.Children.Add(listeningToggles);
        var resizeLyrics = Button("Resize lyrics");
        resizeLyrics.Click += (_, _) =>
        {
            settings.LyricsPlaced = false;
            lyrics.IsChecked = true;
            changed();
        };
        listening.Children.Add(resizeLyrics);
        listening.Children.Add(Row("Lyrics text color", ColorButton("Lyrics text color", settings.LyricsColor, value => settings.LyricsColor = value)));
        var import = Button("Import lyrics for current track");
        import.Click += (_, _) =>
        {
            var message = importLyrics();
            if (message.Length > 0) status.Text = message;
            lyrics.IsChecked = settings.Lyrics;
        };
        listening.Children.Add(import);
        connect = Button(spotify.Connected ? "Disconnect" : "Log in with Spotify");
        connect.Click += async (_, _) =>
        {
            if (spotify.Connected)
            {
                try { spotify.Disconnect(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    status.Text = "Windows could not remove the saved Spotify session. Try again.";
                    return;
                }
                connect.Content = "Log in with Spotify";
                status.Text = "Spotify disconnected.";
                changed();
                return;
            }
            connect.IsEnabled = false;
            status.Text = "Complete authorization in your browser.";
            try
            {
                await spotify.ConnectAsync();
                connect.Content = "Disconnect";
                status.Text = spotify.Remembered ? "Spotify connected." : "Connected for this session. Windows could not save the sign-in.";
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.Net.Http.HttpRequestException
                or System.Net.Sockets.SocketException or IOException or InvalidOperationException
                or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                status.Text = ex is OperationCanceledException
                    ? "Authorization timed out. Try again after checking the redirect URI."
                    : "Spotify connection failed. Check your account access and try again.";
            }
            finally { connect.IsEnabled = true; }
        };
        listening.Children.Add(connect);
        listening.Children.Add(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
    }

    public event Action? PlaceDefaultsRequested;

    private void ImportPhoto()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Multiselect = true };
        if (picker.ShowDialog(this) != true) return;
        foreach (var file in picker.FileNames)
        {
            try
            {
                using var stream = File.OpenRead(file);
                if (stream.Length > 30_000_000) throw new IOException();
                Playback.Decode(stream);
                settings.Photos.Add(new("photo-" + Guid.NewGuid().ToString("N"), Settings.Import(file), Path.GetFileName(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
            {
                MessageBox.Show(this, $"Could not import {Path.GetFileName(file)}. Choose a supported image under 30 MB.", "Archopy");
            }
        }
        changed();
        RefreshPhotos();
    }

    private void RefreshPhotos()
    {
        photoList.Children.Clear();
        foreach (var photo in settings.Photos)
        {
            var remove = Button("Remove");
            AutomationProperties.SetName(remove, "Remove " + photo.Name);
            remove.Click += (_, _) =>
            {
                settings.Photos.Remove(photo);
                settings.Positions.Remove(photo.Id);
                changed();
                RefreshPhotos();
            };
            photoList.Children.Add(Row(photo.Name, remove));
            var edit = new StackPanel();
            edit.Children.Add(Toggle("Show widget frame", photo.Framed, enabled => photo.Framed = enabled));
            edit.Children.Add(Adjustment("Window width", 120, 1000, 1, photo.Width, value => photo.Width = (int)value));
            edit.Children.Add(Adjustment("Image height", 80, 800, 1, photo.Height, value => photo.Height = (int)value));
            edit.Children.Add(Adjustment("Crop zoom", 1, 4, .05, photo.Zoom, value => photo.Zoom = value));
            edit.Children.Add(Adjustment("Crop horizontally", 0, 1, .01, photo.FocusX, value => photo.FocusX = value));
            edit.Children.Add(Adjustment("Crop vertically", 0, 1, .01, photo.FocusY, value => photo.FocusY = value));
            photoList.Children.Add(new Expander { Header = "Edit image", Content = edit, Foreground = Ink });
        }
    }

    private FrameworkElement Adjustment(string label, double min, double max, double step, double initial, Action<double> set)
    {
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            Width = 165, Value = Math.Clamp(initial, min, max), AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft };
        slider.ValueChanged += (_, _) => { set(slider.Value); changed(); };
        return Row(label, slider);
    }

    private Button ColorButton(string name, string value, Action<string> set)
    {
        var button = Button(value);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            var color = Widget.ParseColor((string)button.Content, Colors.SlateGray);
            dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var selected = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            button.Content = selected;
            set(selected);
            changed();
        };
        return button;
    }

    private CheckBox Toggle(string label, bool value, Action<bool> set)
    {
        var check = new CheckBox
        {
            Content = label, IsChecked = value, Foreground = Ink, VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 28, Margin = new Thickness(0, 1, 8, 3), HorizontalAlignment = HorizontalAlignment.Left
        };
        check.Checked += (_, _) => { set(true); changed(); };
        check.Unchecked += (_, _) => { set(false); changed(); };
        return check;
    }

    private StackPanel Section(Panel parent, string title)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        parent.Children.Add(new Border
        {
            Background = Tile, Padding = new Thickness(18, 13, 18, 13), CornerRadius = new CornerRadius(17),
            Margin = new Thickness(0, 0, 0, 12), Child = body
        });
        return body;
    }

    private DockPanel Row(string title, FrameworkElement field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
        DockPanel.SetDock(field, Dock.Right);
        row.Children.Add(field);
        row.Children.Add(new TextBlock
        {
            Text = title, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        AutomationProperties.SetName(field, title);
        return row;
    }

    private void ApplyPalette()
    {
        var dark = settings.Dark;
        Background = dark ? Brushes.Black : Brushes.White;
        Ink.Color = dark ? Colors.White : Colors.Black;
        byte muted = dark ? (byte)185 : (byte)86;
        byte tile = dark ? (byte)19 : (byte)246;
        Muted.Color = Color.FromRgb(muted, muted, muted);
        Tile.Color = Color.FromRgb(tile, tile, tile);
        Resources["PillFill"] = dark ? Brushes.Black : Brushes.White;
        Resources["PillInk"] = dark ? Brushes.White : Brushes.Black;
    }

    private static WrapPanel Choices(string[] values, string selected, Action<string> change)
    {
        var panel = new WrapPanel();
        var group = Guid.NewGuid().ToString();
        foreach (var value in values)
        {
            var choice = new RadioButton { Content = value, IsChecked = selected == value, GroupName = group };
            choice.Checked += (_, _) => change(value);
            panel.Children.Add(choice);
        }
        return panel;
    }

    private static Button Button(string label) => new()
    {
        Content = label, Padding = new Thickness(9, 3, 9, 3), MinHeight = 30,
        Margin = new Thickness(0, 2, 7, 2),
        HorizontalAlignment = HorizontalAlignment.Left
    };
}
