using System.Globalization;

namespace Tessitura.IO.Tests.MusicXml;

/// <summary>Where a corpus file comes from and whether Tessitura 1.0 is expected to preserve it.</summary>
/// <param name="Path">The absolute file path.</param>
/// <param name="RelativePath">The path relative to the corpus root, with forward slashes.</param>
/// <param name="Source">"public", "dorico", "sibelius" or "musescore".</param>
/// <param name="Group">The test-suite group, such as "03 Ritmo".</param>
/// <param name="InScope">Whether the file is inside the F3 scope.</param>
/// <param name="Reason">Why an out-of-scope file is excluded; empty otherwise.</param>
public sealed record CorpusFile(string Path, string RelativePath, string Source, string Group, bool InScope, string Reason);

/// <summary>Finds the corpus files and classifies them by the F3 scope.</summary>
public static class MusicXmlCorpus
{
    // Proposed F3 scope (pending owner approval): what the current model can represent —
    // written pitches, rests, rhythm, chords, ties, meters, clefs, key signatures, parts, staves and voices.
    private static readonly Dictionary<string, string> GroupNames = new()
    {
        ["01"] = "01 Alturas", ["02"] = "02 Silencios", ["03"] = "03 Ritmo", ["11"] = "11 Compases",
        ["12"] = "12 Claves", ["13"] = "13 Armaduras", ["14"] = "14 Detalles de pentagrama",
        ["21"] = "21 Acordes", ["22"] = "22 Cabezas de nota", ["23"] = "23 Tresillos y grupos",
        ["24"] = "24 Notas de adorno", ["31"] = "31 Indicaciones", ["32"] = "32 Notaciones",
        ["33"] = "33 Ligaduras y líneas", ["34"] = "34 Aspecto", ["41"] = "41 Partes y grupos",
        ["42"] = "42 Varias voces", ["43"] = "43 Varios pentagramas", ["45"] = "45 Repeticiones",
        ["46"] = "46 Barras y compases", ["51"] = "51 Cabecera", ["52"] = "52 Página",
        ["61"] = "61 Letra", ["71"] = "71 Cifrado e instrumentos", ["72"] = "72 Transpositores",
        ["73"] = "73 Percusión", ["74"] = "74 Bajo cifrado", ["75"] = "75 Acordeón",
        ["90"] = "90 Formatos", ["99"] = "99 Casos difíciles",
    };

    private static readonly HashSet<string> InScopeGroups =
        ["01", "02", "03", "11", "12", "13", "21", "41", "42", "43", "90"];

    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["01d"] = "microtonos", ["01f"] = "microtonos", ["01g"] = "alteraciones de flecha", ["01h"] = "microtonos",
        ["02c"] = "silencios de varios compases", ["02d"] = "silencios de varios compases",
        ["03a"] = "notas más largas que una redonda", ["11d"] = "notas más largas que una redonda",
        ["03e-Rhythm-No"] = "sin divisions", ["11b"] = "sin compás", ["11f"] = "símbolos de compás", ["11h"] = "sin medida",
        ["11i"] = "compás alternativo", ["13c"] = "armadura no tradicional", ["13d"] = "microtonos",
        ["13e-KeySignatures-Mid"] = "cambio de armadura a mitad de compás", ["21g"] = "trémolos",
        ["33b"] = "", ["33i"] = "", ["33k"] = "",
        ["41c"] = "grupos de pentagramas", ["41d"] = "grupos de pentagramas", ["41e"] = "grupos de pentagramas",
        ["41f"] = "grupos de pentagramas", ["41g"] = "grupos de pentagramas", ["41h"] = "demasiadas partes",
        ["41i"] = "nombres de parte", ["41j"] = "nombres de parte", ["41k"] = "nombres de parte",
        ["41l"] = "nombres de grupo", ["43d"] = "cambio de pentagrama", ["43e"] = "dinámicas",
        ["43f"] = "letra", ["43g"] = "símbolo de parte", ["43i"] = "cambio de pentagrama",
        ["42a"] = "letra", ["42b"] = "cambio de clave a mitad de compás",
    };

    /// <summary>Gets the corpus root inside the test output directory.</summary>
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Corpus");

    /// <summary>Lists every corpus file, in a stable order.</summary>
    /// <returns>The classified files.</returns>
    public static IReadOnlyList<CorpusFile> Enumerate()
    {
        List<CorpusFile> files = [];
        if (!Directory.Exists(Root))
        {
            return files;
        }

        foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".musicxml" or ".xml" or ".mxl"))
            {
                continue;
            }

            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            string source = relative.Split('/')[0] == "real" ? relative.Split('/')[1] : "public";
            files.Add(source == "public" ? Classify(path, relative) : new CorpusFile(path, relative, source, "Real", true, ""));
        }

        return files;
    }

    /// <summary>Classifies a public test-suite file by its number prefix.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="relative">The path relative to the corpus root.</param>
    /// <returns>The classified file.</returns>
    public static CorpusFile Classify(string path, string relative)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string number = name.Length >= 2 ? name[..2] : "";
        string group = GroupNames.GetValueOrDefault(number, "Otros");
        if (name.Contains(".invalid", StringComparison.Ordinal))
        {
            return new CorpusFile(path, relative, "public", group, false, "archivo inválido a propósito");
        }

        foreach ((string prefix, string reason) in Exceptions)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return reason.Length == 0
                    ? new CorpusFile(path, relative, "public", group, true, "")
                    : new CorpusFile(path, relative, "public", group, false, reason);
            }
        }

        return InScopeGroups.Contains(number)
            ? new CorpusFile(path, relative, "public", group, true, "")
            : new CorpusFile(path, relative, "public", group, false, string.Create(CultureInfo.InvariantCulture,
                $"fuera del alcance de F3 ({group})"));
    }
}
