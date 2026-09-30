using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Adds an articulation to an event, or removes it when the event already has it.</summary>
/// <param name="Target">The event.</param>
/// <param name="Kind">The articulation.</param>
public sealed record ToggleArticulationCommand(EventId Target, ArticulationKind Kind) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Toggle articulation";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArticulationAttachment mark = new(Target, Kind);
        ImmutableArray<Attachment> list = score.AttachmentList;
        return score with { Attachments = list.Contains(mark) ? list.Remove(mark) : list.Add(mark) };
    }
}

/// <summary>Adds an attachment; a dynamic, tempo or chord symbol replaces the one of its kind already on the event.</summary>
/// <param name="Attachment">The attachment to add.</param>
public sealed record AddAttachmentCommand(Attachment Attachment) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Add attachment";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        ImmutableArray<Attachment> list = [.. score.AttachmentList.Where(a =>
            a.Target != Attachment.Target || a.GetType() != Attachment.GetType() || Attachment is TextAttachment)];
        return score with { Attachments = list.Add(Attachment) };
    }
}

/// <summary>Removes every attachment of a kind from an event.</summary>
/// <param name="Target">The event.</param>
/// <param name="AttachmentType">The attachment type to remove.</param>
public sealed record RemoveAttachmentsCommand(EventId Target, Type AttachmentType) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Remove attachments";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        return score with { Attachments = [.. score.AttachmentList.Where(a => a.Target != Target || a.GetType() != AttachmentType)] };
    }
}

/// <summary>Sets the dynamic level that starts at an event, replacing any level already there.</summary>
/// <param name="Target">The event.</param>
/// <param name="Level">The level, or null to remove the dynamic.</param>
public sealed record SetDynamicCommand(EventId Target, DynamicLevel? Level) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Set dynamic";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        ImmutableArray<Attachment> list = [.. score.AttachmentList.Where(a => a is not DynamicAttachment d || d.Target != Target)];
        if (Level is DynamicLevel level)
        {
            list = list.Add(new DynamicAttachment(Target, level));
        }

        return score with { Attachments = list };
    }
}
