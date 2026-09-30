namespace Tessitura.App;

/// <summary>Identifies the editor's score presentation.</summary>
public enum ScoreViewMode
{
    /// <summary>Shows one paginated score page at a time.</summary>
    Page,
    /// <summary>Shows the full score as one continuous, unpaginated galley.</summary>
    Continuous,
    /// <summary>Shows one linked instrument part.</summary>
    Part,
}
