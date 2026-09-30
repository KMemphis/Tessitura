using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Adds a lyric or replaces the lyric in the same verse on its target event.</summary>
/// <param name="Attachment">The lyric syllable or extender mark.</param>
public sealed record SetLyricCommand(LyricAttachment Attachment) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Set lyric";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(Attachment);
        ImmutableArray<Attachment> lyrics = [.. score.AttachmentList.Where(existing =>
            existing is not LyricAttachment lyric || lyric.Target != Attachment.Target || lyric.Verse != Attachment.Verse)];
        return score with { Attachments = lyrics.Add(Attachment) };
    }
}

/// <summary>Removes a lyric verse from one event.</summary>
/// <param name="Target">The event.</param>
/// <param name="Verse">The one-based verse number.</param>
public sealed record RemoveLyricCommand(EventId Target, int Verse) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Remove lyric";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (Verse < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Verse));
        }

        return score with
        {
            Attachments = [.. score.AttachmentList.Where(existing =>
                existing is not LyricAttachment lyric || lyric.Target != Target || lyric.Verse != Verse)],
        };
    }
}
