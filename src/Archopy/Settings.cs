using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Archopy;

internal sealed class Settings
{
    public bool Dark { get; set; }
    public bool Utc { get; set; }
    public bool SnapToScreen { get; set; } = true;
    public bool HorizontalTime { get; set; }
    public bool Clock { get; set; } = true;
    public bool Spotify { get; set; } = true;
    public bool Pinned { get; set; } = true;
    public bool Calendar { get; set; }
    public bool Date { get; set; }
    public bool Lyrics { get; set; }
    public string Surface { get; set; } = "Glass";
    public string Color { get; set; } = "#37476A";
    public string GradientEnd { get; set; } = "#765398";
    public double ClockScale { get; set; } = 1;
    public double ClockStretch { get; set; } = 1;
    public int LyricsWidth { get; set; } = 500;
    public int LyricsHeight { get; set; } = 108;
    public bool LyricsPlaced { get; set; }
    public string LyricsColor { get; set; } = "#FFFFFF";
    public List<Photo> Photos { get; set; } = new();
    public Dictionary<string, Position> Positions { get; set; } = new();
    public Dictionary<string, string> LyricFiles { get; set; } = new();

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archopy");

    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (IOException) { return new(); }
        catch (JsonException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, FilePath, true);
    }

    public static string Import(string source)
    {
        var directory = Path.Combine(DirectoryPath, "imports");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        File.Copy(source, target);
        return target;
    }
}

internal sealed record Position(int X, int Y);
internal sealed record Photo(string Id, string Path, string Name)
{
    public int Width { get; set; } = 245;
    public int Height { get; set; } = 160;
    public bool Framed { get; set; } = true;
    public double Zoom { get; set; } = 1;
    public double FocusX { get; set; } = .5;
    public double FocusY { get; set; } = .5;
}
