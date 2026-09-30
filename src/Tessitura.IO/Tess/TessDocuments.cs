using System.Text.Json.Serialization;

namespace Tessitura.IO.Tess;

/// <summary>Describes the format and producing application of a .tess archive.</summary>
/// <param name="FormatVersion">The .tess format version of the archive.</param>
/// <param name="AppVersion">The application version that wrote the archive.</param>
public sealed record TessManifest(int FormatVersion, string AppVersion);

internal sealed record NoteDto(int Step, int Alter, int Octave, bool Tied);

internal sealed record EventDto(
    string Kind, Guid Id, long OnsetNum, long OnsetDen, int Value, int Dots, int Stem,
    List<NoteDto> Notes, int Actual = 0, int Normal = 0, List<EventDto>? Children = null);

internal sealed record VoiceDto(int Number, List<EventDto> Events);

internal sealed record StaffMeasureDto(int Staff, int Measure, List<VoiceDto> Voices);

internal sealed record StaffDto(string Name, int Clef);

internal sealed record InstrumentDto(string Name, List<StaffDto> Staves);

internal sealed record MeasureDto(int Number, int Numerator, int Denominator, int Fifths);

internal sealed record AttachmentDto(string Kind, Guid Target, int Value);

internal sealed record ScoreDto(
    string Title, string Composer, List<InstrumentDto> Instruments,
    List<MeasureDto> Measures, List<StaffMeasureDto> Content, List<AttachmentDto>? Attachments = null);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TessManifest))]
[JsonSerializable(typeof(ScoreDto))]
internal sealed partial class TessJsonContext : JsonSerializerContext;
