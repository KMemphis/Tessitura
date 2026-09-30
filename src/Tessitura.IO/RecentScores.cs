using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tessitura.IO;

/// <summary>Remembers one recently opened score.</summary>
/// <param name="Path">The full path of the .tess file.</param>
/// <param name="Title">The title shown on the start screen.</param>
/// <param name="LastOpened">When the file was last opened or saved.</param>
public sealed record RecentScore(string Path, string Title, DateTimeOffset LastOpened);

[JsonSerializable(typeof(List<RecentScore>))]
internal sealed partial class RecentScoresJsonContext : JsonSerializerContext;

/// <summary>Stores the most recently used scores in a small JSON file.</summary>
public sealed class RecentScores
{
    /// <summary>The number of entries kept.</summary>
    public const int Capacity = 10;

    private readonly string _path;
    private List<RecentScore> _items;

    /// <summary>Loads the list, treating a missing or unreadable file as empty.</summary>
    /// <param name="path">The JSON file path.</param>
    public RecentScores(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = Path.GetFullPath(path);
        _items = Load(_path);
    }

    /// <summary>Gets the entries, most recent first.</summary>
    public IReadOnlyList<RecentScore> Items => _items;

    /// <summary>Records a file as the most recent one and saves the list.</summary>
    /// <param name="scorePath">The .tess path.</param>
    /// <param name="title">The score title.</param>
    /// <param name="when">The timestamp.</param>
    public void Add(string scorePath, string title, DateTimeOffset when)
    {
        ArgumentException.ThrowIfNullOrEmpty(scorePath);
        string full = Path.GetFullPath(scorePath);
        List<RecentScore> updated = [new RecentScore(full, title, when)];
        foreach (RecentScore item in _items)
        {
            if (item.Path != full && updated.Count < Capacity)
            {
                updated.Add(item);
            }
        }

        _items = updated;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
            _items, RecentScoresJsonContext.Default.ListRecentScore));
        File.Move(temporary, _path, overwrite: true);
    }

    private static List<RecentScore> Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path),
                RecentScoresJsonContext.Default.ListRecentScore) ?? [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
