using System.IO.Compression;

namespace Tessitura.App;

/// <summary>Materializes the included General MIDI SoundFont in the per-user cache.</summary>
public static class BundledSoundFont
{
    private const long ExpectedLength = 124331092;

    /// <summary>Returns a playable SF2 file, extracting the bundled Brotli archive once.</summary>
    /// <param name="applicationDirectory">Directory containing the application assets.</param>
    /// <param name="cacheDirectory">Writable per-user cache directory.</param>
    public static string EnsureAvailable(string applicationDirectory, string cacheDirectory)
    {
        string archive = Path.Combine(applicationDirectory, "assets", "soundfonts", "default.sf2.br");
        if (!File.Exists(archive))
        {
            throw new FileNotFoundException("No se encuentra el SoundFont incluido con Tessitura.", archive);
        }

        Directory.CreateDirectory(cacheDirectory);
        string target = Path.Combine(cacheDirectory, "FluidR3Mono_GM-2.315.sf2");
        if (IsValid(target)) return target;

        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream source = File.OpenRead(archive))
            using (BrotliStream decoder = new(source, CompressionMode.Decompress))
            using (FileStream output = File.Create(temporary))
            {
                decoder.CopyTo(output);
            }

            if (!IsValid(temporary))
            {
                throw new InvalidDataException("El SoundFont incluido está incompleto.");
            }

            File.Move(temporary, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsValid(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != ExpectedLength) return false;
        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        return stream.Read(header) == 4 && header.SequenceEqual("RIFF"u8);
    }
}
