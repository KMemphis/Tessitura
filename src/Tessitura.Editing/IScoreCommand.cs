using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Describes an immutable transformation of a score snapshot.</summary>
public interface IScoreCommand
{
    /// <summary>Gets the user-facing description of this edit.</summary>
    string Description { get; }

    /// <summary>Applies the edit and returns a new score snapshot.</summary>
    /// <param name="score">The score snapshot to edit.</param>
    /// <param name="context">The staff, measure and voice targeted by this edit.</param>
    /// <returns>The edited score snapshot.</returns>
    Score Apply(Score score, EditContext context);
}
