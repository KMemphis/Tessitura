using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tessitura.Core;
using Tessitura.Engraving;

namespace Tessitura.IO.Tess;

/// <summary>Contains everything stored in a .tess archive.</summary>
/// <param name="Score">The music.</param>
/// <param name="Style">The engraving style.</param>
/// <param name="Manifest">The manifest as stored, before migration.</param>
public sealed record TessDocument(Score Score, Style Style, TessManifest Manifest);

/// <summary>Reads and writes the native .tess ZIP format.</summary>
public static class TessFile
{
    private const string ManifestEntry = "manifest.json";
    private const string ScoreEntry = "score.json";
    private const string StyleEntry = "style.json";

    /// <summary>Writes an archive atomically: to a temporary sibling file, then renamed over the target.</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="score">The score to store.</param>
    /// <param name="style">The style to store.</param>
    /// <param name="appVersion">The application version recorded in the manifest.</param>
    public static void Save(string path, Score score, Style style, string appVersion = "0.0.0")
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(style);
        string fullPath = Path.GetFullPath(path);
        string temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true);
                WriteEntry(archive, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(
                    new TessManifest(TessMigrator.CurrentVersion, appVersion),
                    TessJsonContext.Default.TessManifest));
                WriteEntry(archive, ScoreEntry, JsonSerializer.SerializeToUtf8Bytes(
                    ScoreMapper.ToDto(score), TessJsonContext.Default.ScoreDto));
                WriteEntry(archive, StyleEntry, JsonSerializer.SerializeToUtf8Bytes(
                    style, StyleJsonContext.Default.Style));
                archive.Dispose();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Opens an archive, migrating older format versions to the current one.</summary>
    /// <param name="path">The archive path.</param>
    /// <returns>The stored document.</returns>
    public static TessDocument Open(string path) => Open(path, new TessMigrator());

    /// <summary>Opens an archive using an explicit migrator.</summary>
    /// <param name="path">The archive path.</param>
    /// <param name="migrator">The migrator to apply.</param>
    /// <returns>The stored document.</returns>
    public static TessDocument Open(string path, TessMigrator migrator)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(migrator);
        using ZipArchive archive = ZipFile.OpenRead(path);
        TessManifest manifest = JsonSerializer.Deserialize(ReadEntry(archive, ManifestEntry),
            TessJsonContext.Default.TessManifest)
            ?? throw new InvalidDataException("The manifest is empty.");
        JsonObject scoreNode = JsonNode.Parse(ReadEntry(archive, ScoreEntry)) as JsonObject
            ?? throw new InvalidDataException("score.json is not an object.");
        migrator.Migrate(scoreNode, manifest.FormatVersion);
        ScoreDto dto = scoreNode.Deserialize(TessJsonContext.Default.ScoreDto)
            ?? throw new InvalidDataException("score.json is empty.");
        Style style = JsonSerializer.Deserialize(ReadEntry(archive, StyleEntry),
            StyleJsonContext.Default.Style)
            ?? throw new InvalidDataException("style.json is empty.");
        return new TessDocument(ScoreMapper.FromDto(dto), style, manifest);
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name)
            ?? throw new InvalidDataException($"The archive has no {name}.");
        using Stream stream = entry.Open();
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
