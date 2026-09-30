using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Tessitura.Editing;

namespace Tessitura.App;

/// <summary>A small entry box for dynamics, tempo marks, chord symbols, text and lyrics.</summary>
public sealed class TextPopover : Border
{
    private readonly ScoreInputController _input;
    private readonly Action _onClosed;
    private readonly TextBox _box;
    private readonly TextBlock _message;

    /// <summary>Creates the popover.</summary>
    /// <param name="input">The editor that receives the result.</param>
    /// <param name="onClosed">Called after closing, to restore focus.</param>
    public TextPopover(ScoreInputController input, Action onClosed)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _onClosed = onClosed ?? throw new ArgumentNullException(nameof(onClosed));
        _box = new TextBox();
        _message = new TextBlock { FontSize = 12 };
        _box.KeyDown += OnKeyDown;
        Child = new StackPanel { Spacing = 6, Children = { _message, _box } };
        Padding = new Thickness(10);
        BorderThickness = new Thickness(1);
        Width = 360;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 80);
        IsVisible = false;
    }

    /// <summary>Gets whether the popover is shown.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>Gets what the popover is asking for.</summary>
    public TextEntryKind Kind { get; private set; }

    /// <summary>Gets or sets the typed text.</summary>
    public string Text
    {
        get => _box.Text ?? string.Empty;
        set => _box.Text = value;
    }

    /// <summary>Gets the hint or error shown above the box.</summary>
    public string Message => _message.Text ?? string.Empty;

    /// <summary>Opens the popover for a kind of entry.</summary>
    /// <param name="kind">The entry kind.</param>
    public void Open(TextEntryKind kind)
    {
        Kind = kind;
        _message.Text = kind switch
        {
            TextEntryKind.Dynamic => "Dinámica: ppp, pp, p, mp, mf, f, ff o fff",
            TextEntryKind.Tempo => "Tempo: por ejemplo q=120 o e.=60",
            TextEntryKind.Lyric => "Letra: estrofa:sílaba (1:glo-, 2:gloria); ~ inicia, ~> continúa y ~ termina un extensor",
            _ => "Texto o cifrado: por ejemplo Cmaj7, F#m7/A o dolce",
        };
        _box.Text = string.Empty;
        IsVisible = true;
        _box.Focus();
    }

    /// <summary>Applies the typed text to the selected event.</summary>
    /// <returns>Whether it was understood; the popover closes only then.</returns>
    public bool Submit()
    {
        if (_input.SubmitText(Kind, Text))
        {
            Close();
            return true;
        }

        _message.Text = _input.SelectedEventProperties is null
            ? "Selecciona un evento de la partitura primero"
            : $"No se entiende «{Text}»";
        return false;
    }

    /// <summary>Closes the popover and restores focus to the editor.</summary>
    public void Close()
    {
        if (IsVisible)
        {
            IsVisible = false;
            _onClosed();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
