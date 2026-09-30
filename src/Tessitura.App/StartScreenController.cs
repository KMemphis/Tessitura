using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.IO;

namespace Tessitura.App;

/// <summary>Holds the start-screen state and exposes its user actions.</summary>
public sealed class StartScreenController
{
    private readonly RecentScores _recents;

    /// <summary>Creates the controller.</summary>
    /// <param name="recents">The persistent list of recent scores.</param>
    public StartScreenController(RecentScores recents)
    {
        _recents = recents ?? throw new ArgumentNullException(nameof(recents));
    }

    /// <summary>Raised when the wizard produced a new score.</summary>
    public event EventHandler<Score>? ScoreCreated;

    /// <summary>Raised when a recent score was chosen.</summary>
    public event EventHandler<string>? RecentChosen;

    /// <summary>Raised when the user asks to browse for a file.</summary>
    public event EventHandler? OpenFileRequested;

    /// <summary>Raised when the visible page or its options change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Gets whether the new-score wizard is shown instead of the home page.</summary>
    public bool IsWizardOpen { get; private set; }

    /// <summary>Gets the wizard options, which are edited before creating the score.</summary>
    public NewScoreOptions Options { get; set; } = NewScoreOptions.CreateDefault(ScoreTemplate.Piano);

    /// <summary>Gets the recent scores, most recent first.</summary>
    public IReadOnlyList<RecentScore> Recents => _recents.Items;

    /// <summary>Defines the actions of the start screen.</summary>
    /// <returns>The registered action definitions.</returns>
    public ImmutableArray<ActionDefinition> CreateActions()
    {
        ImmutableArray<ActionDefinition>.Builder actions = ImmutableArray.CreateBuilder<ActionDefinition>();
        actions.Add(new("start.template.piano", "Nueva partitura de piano", "Ctrl+1",
            () => OpenWizard(ScoreTemplate.Piano)));
        actions.Add(new("start.template.string-quartet", "Nueva partitura de cuarteto de cuerda", "Ctrl+2",
            () => OpenWizard(ScoreTemplate.StringQuartet)));
        actions.Add(new("start.template.satb", "Nueva partitura de coro SATB", "Ctrl+3",
            () => OpenWizard(ScoreTemplate.ChoirSatb)));
        actions.Add(new("start.create", "Crear la partitura", "Ctrl+Enter", Create));
        actions.Add(new("start.back", "Volver al inicio", "Esc", CloseWizard));
        actions.Add(new("start.open", "Abrir un archivo", "Ctrl+O",
            () => OpenFileRequested?.Invoke(this, EventArgs.Empty)));
        for (int index = 0; index < RecentScores.Capacity; index++)
        {
            int recentIndex = index;
            actions.Add(new($"start.recent.{index}", $"Abrir reciente {index + 1}",
                index < 9 ? $"Ctrl+Alt+{index + 1}" : "Ctrl+Alt+0", () => OpenRecent(recentIndex)));
        }

        return actions.ToImmutable();
    }

    private void OpenWizard(ScoreTemplate template)
    {
        Options = NewScoreOptions.CreateDefault(template);
        IsWizardOpen = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseWizard()
    {
        IsWizardOpen = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Create()
    {
        if (!IsWizardOpen)
        {
            return;
        }

        ScoreCreated?.Invoke(this, NewScoreFactory.Create(Options));
    }

    private void OpenRecent(int index)
    {
        if (index < _recents.Items.Count)
        {
            RecentChosen?.Invoke(this, _recents.Items[index].Path);
        }
    }
}
