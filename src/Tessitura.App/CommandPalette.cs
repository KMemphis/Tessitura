using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Tessitura.App;

/// <summary>Overlay that lists every registered action and runs the chosen one.</summary>
public sealed class CommandPalette : Border
{
    private readonly ActionRegistry _actions;
    private readonly Action _onClosed;
    private readonly TextBox _input;
    private readonly ListBox _list;
    private ImmutableArray<RegisteredAction> _results = [];

    /// <summary>Creates the palette over a registry.</summary>
    /// <param name="actions">The registry whose actions are searched and executed.</param>
    /// <param name="onClosed">Called after the palette closes, to restore focus.</param>
    public CommandPalette(ActionRegistry actions, Action onClosed)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _onClosed = onClosed ?? throw new ArgumentNullException(nameof(onClosed));
        _input = new TextBox { PlaceholderText = "Buscar una acción…" };
        _list = new ListBox { MaxHeight = 360 };
        _input.TextChanged += (_, _) => Refresh();
        _input.KeyDown += OnInputKeyDown;
        _list.DoubleTapped += (_, _) => ExecuteSelected();
        Child = new StackPanel { Spacing = 6, Children = { _input, _list } };
        Padding = new Thickness(10);
        BorderThickness = new Thickness(1);
        Width = 560;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 70, 0, 0);
        IsVisible = false;
    }

    /// <summary>Gets whether the palette is shown.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>Gets or sets the search text.</summary>
    public string Query
    {
        get => _input.Text ?? string.Empty;
        set
        {
            _input.Text = value;
            Refresh();
        }
    }

    /// <summary>Gets the actions matching the current query, best first.</summary>
    public ImmutableArray<RegisteredAction> Results => _results;

    /// <summary>Gets or sets the highlighted result.</summary>
    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set => _list.SelectedIndex = value;
    }

    /// <summary>Shows the palette with an empty query.</summary>
    public void Open()
    {
        IsVisible = true;
        _input.Text = string.Empty;
        Refresh();
        _input.Focus();
    }

    /// <summary>Hides the palette and restores focus to the editor.</summary>
    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        _onClosed();
    }

    /// <summary>Runs the highlighted action and closes the palette.</summary>
    /// <returns>Whether an action was executed.</returns>
    public bool ExecuteSelected()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _results.Length)
        {
            return false;
        }

        string id = _results[index].Id;
        Close();
        return _actions.TryExecute(id);
    }

    private void Refresh()
    {
        _results = CommandSearch.Search(_actions.Actions, Query);
        string[] rows = new string[_results.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            RegisteredAction action = _results[index];
            rows[index] = action.Shortcut.Length == 0 ? action.Name : $"{action.Name}    ({action.Shortcut})";
        }

        _list.ItemsSource = rows;
        _list.SelectedIndex = rows.Length > 0 ? 0 : -1;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter:
                ExecuteSelected();
                break;
            case Key.Down:
                _list.SelectedIndex = Math.Min(_results.Length - 1, _list.SelectedIndex + 1);
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                break;
            default:
                // Ctrl+K toggles the palette closed even while the search box has focus.
                if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control)
                {
                    Close();
                    break;
                }

                return;
        }

        e.Handled = true;
    }
}
