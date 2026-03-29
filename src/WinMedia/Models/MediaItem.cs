using System;
using System.IO;
using System.Windows.Media;

namespace WinMedia.Models;

public sealed class MediaItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Extension { get; init; }
    public required string MediaType { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ModifiedAt { get; init; }
    public required long SizeBytes { get; init; }
    public ImageSource? Thumbnail { get; init; }

    public bool IsImage => MediaType == "Image";
    public bool IsVideo => MediaType == "Video";
    public string FolderName => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public string DisplaySize => FormatSize(SizeBytes);
    public string ModifiedLabel => ModifiedAt.ToString("yyyy-MM-dd HH:mm");
    public string CreatedLabel => CreatedAt.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int index = 0;

        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}
