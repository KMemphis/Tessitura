using System.Collections.Immutable;
using System.Text.Json;
using Avalonia.Input;

namespace Tessitura.App;

/// <summary>Defines a named application action and the behavior it invokes.</summary>
public sealed record ActionDefinition(
    string Id,
    string Name,
    string DefaultShortcut,
    Action Execute);

/// <summary>Describes an action with the shortcut currently assigned to it.</summary>
public sealed record RegisteredAction(string Id, string Name, string Shortcut);

/// <summary>Dispatches configured keyboard shortcuts and actions by stable identifier.</summary>
public sealed class ActionRegistry
{
    private const int CurrentConfigurationVersion = 1;
    private const KeyModifiers SupportedModifiers =
        KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;

    private readonly Dictionary<string, ActionDefinition> _actionsById;
    private readonly Dictionary<ShortcutGesture, ActionDefinition> _actionsByShortcut;

    private ActionRegistry(
        Dictionary<string, ActionDefinition> actionsById,
        Dictionary<ShortcutGesture, ActionDefinition> actionsByShortcut,
        ImmutableArray<RegisteredAction> actions)
    {
        _actionsById = actionsById;
        _actionsByShortcut = actionsByShortcut;
        Actions = actions;
    }

    /// <summary>Gets the registered actions and their effective shortcuts.</summary>
    public ImmutableArray<RegisteredAction> Actions { get; }

    /// <summary>Loads the JSON shortcut file or creates it with the action defaults.</summary>
    /// <param name="actions">Actions available to the application.</param>
    /// <param name="settingsPath">Path to the user-editable JSON settings file.</param>
    /// <returns>A registry with validated shortcuts and dispatch handlers.</returns>
    /// <exception cref="InvalidDataException">The settings file is malformed or invalid.</exception>
    /// <exception cref="InvalidOperationException">Action IDs or shortcuts conflict.</exception>
    public static ActionRegistry LoadOrCreate(
        IEnumerable<ActionDefinition> actions,
        string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        string fullPath = Path.GetFullPath(settingsPath);
        Dictionary<string, ActionDefinition> actionsById = new(StringComparer.Ordinal);
        Dictionary<string, string> effectiveShortcuts = new(StringComparer.Ordinal);

        foreach (ActionDefinition? action in actions)
        {
            if (action is null)
            {
                throw new ArgumentException("The action list cannot contain null entries.", nameof(actions));
            }

            ValidateAction(action);
            if (!actionsById.TryAdd(action.Id, action))
            {
                throw new InvalidOperationException($"Action ID '{action.Id}' is registered more than once.");
            }

            _ = ParseShortcut(action.DefaultShortcut);
            effectiveShortcuts.Add(action.Id, action.DefaultShortcut);
        }

        bool createConfiguration = !File.Exists(fullPath);
        if (!createConfiguration)
        {
            ApplyConfiguration(fullPath, actionsById, effectiveShortcuts);
        }

        Dictionary<ShortcutGesture, ActionDefinition> actionsByShortcut = [];
        ImmutableArray<RegisteredAction>.Builder registeredActions =
            ImmutableArray.CreateBuilder<RegisteredAction>(actionsById.Count);
        foreach ((string actionId, ActionDefinition action) in actionsById)
        {
            string shortcut = effectiveShortcuts[actionId];
            ShortcutGesture gesture = ParseShortcut(shortcut);
            if (!actionsByShortcut.TryAdd(gesture, action))
            {
                ActionDefinition conflict = actionsByShortcut[gesture];
                throw new InvalidOperationException(
                    $"Shortcut '{shortcut}' is assigned to both '{conflict.Id}' and '{action.Id}'.");
            }

            registeredActions.Add(new RegisteredAction(action.Id, action.Name, shortcut));
        }

        if (createConfiguration)
        {
            WriteDefaultConfiguration(fullPath, effectiveShortcuts);
        }

        return new ActionRegistry(actionsById, actionsByShortcut, registeredActions.MoveToImmutable());
    }

    /// <summary>Executes the action bound to a key and exact modifier combination.</summary>
    /// <param name="key">The Avalonia key that was pressed.</param>
    /// <param name="modifiers">The modifiers held with the key.</param>
    /// <returns><see langword="true"/> when an action was executed.</returns>
    public bool TryExecute(Key key, KeyModifiers modifiers)
    {
        if (key == Key.None || (modifiers & ~SupportedModifiers) != 0)
        {
            return false;
        }

        ShortcutGesture gesture = new(key, modifiers);
        if (!_actionsByShortcut.TryGetValue(gesture, out ActionDefinition? action))
        {
            return false;
        }

        action.Execute();
        return true;
    }

    /// <summary>Executes an action by its stable identifier.</summary>
    /// <param name="actionId">The registered action identifier.</param>
    /// <returns><see langword="true"/> when the action exists and was executed.</returns>
    public bool TryExecute(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (!_actionsById.TryGetValue(actionId, out ActionDefinition? action))
        {
            return false;
        }

        action.Execute();
        return true;
    }

    private static void ValidateAction(ActionDefinition action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.DefaultShortcut);
        ArgumentNullException.ThrowIfNull(action.Execute);

        if (!string.Equals(action.Id, action.Id.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Action IDs cannot start or end with whitespace.", nameof(action));
        }

        if (!string.Equals(action.Name, action.Name.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Action names cannot start or end with whitespace.", nameof(action));
        }
    }

    private static void ApplyConfiguration(
        string settingsPath,
        Dictionary<string, ActionDefinition> actionsById,
        Dictionary<string, string> effectiveShortcuts)
    {
        ShortcutConfiguration? configuration;
        try
        {
            string json = File.ReadAllText(settingsPath);
            configuration = JsonSerializer.Deserialize<ShortcutConfiguration>(json, CreateJsonOptions());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Shortcut settings '{settingsPath}' contain invalid JSON.", exception);
        }

        if (configuration is null || configuration.Version != CurrentConfigurationVersion ||
            configuration.Shortcuts is null)
        {
            throw new InvalidDataException(
                $"Shortcut settings '{settingsPath}' must contain version {CurrentConfigurationVersion} and a shortcuts object.");
        }

        foreach ((string actionId, string? shortcut) in configuration.Shortcuts)
        {
            if (!actionsById.ContainsKey(actionId))
            {
                throw new InvalidDataException(
                    $"Shortcut settings '{settingsPath}' refer to unknown action ID '{actionId}'.");
            }

            if (shortcut is null)
            {
                throw new InvalidDataException(
                    $"Shortcut for action '{actionId}' cannot be null.");
            }

            try
            {
                _ = ParseShortcut(shortcut);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Shortcut '{shortcut}' for action '{actionId}' is invalid.", exception);
            }

            effectiveShortcuts[actionId] = shortcut;
        }
    }

    private static void WriteDefaultConfiguration(string settingsPath, Dictionary<string, string> shortcuts)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidDataException("The shortcut settings path must include a parent directory.");
        }

        Directory.CreateDirectory(directory);
        ShortcutConfiguration configuration = new(CurrentConfigurationVersion, shortcuts);
        string json = JsonSerializer.Serialize(configuration, CreateJsonOptions());
        File.WriteAllText(settingsPath, json);
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static ShortcutGesture ParseShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut) ||
            !string.Equals(shortcut, shortcut.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("A shortcut cannot be empty or padded with whitespace.");
        }

        string[] parts = shortcut.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty))
        {
            throw new InvalidDataException($"Shortcut '{shortcut}' has an empty key or modifier.");
        }

        KeyModifiers modifiers = KeyModifiers.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            KeyModifiers modifier = ParseModifier(parts[index], shortcut);
            if ((modifiers & modifier) != 0)
            {
                throw new InvalidDataException($"Shortcut '{shortcut}' repeats a modifier.");
            }

            modifiers |= modifier;
        }

        string keyName = parts[^1];
        Key key;
        switch (keyName.ToUpperInvariant())
        {
            case "PLUS":
                key = Key.OemPlus;
                modifiers |= KeyModifiers.Shift;
                break;
            case "MINUS":
                key = Key.OemMinus;
                break;
            case "PERIOD":
            case ".":
                key = Key.OemPeriod;
                break;
            case "SPACE":
                key = Key.Space;
                break;
            case "ESC":
            case "ESCAPE":
                key = Key.Escape;
                break;
            case "0":
                key = Key.D0;
                break;
            case "1":
                key = Key.D1;
                break;
            case "2":
                key = Key.D2;
                break;
            case "3":
                key = Key.D3;
                break;
            case "4":
                key = Key.D4;
                break;
            case "5":
                key = Key.D5;
                break;
            case "6":
                key = Key.D6;
                break;
            case "7":
                key = Key.D7;
                break;
            case "8":
                key = Key.D8;
                break;
            case "9":
                key = Key.D9;
                break;
            default:
                if (!Enum.TryParse(keyName, ignoreCase: true, out key) ||
                    !Enum.IsDefined(key) || key == Key.None)
                {
                    throw new InvalidDataException($"Shortcut '{shortcut}' names an unknown key.");
                }

                break;
        }

        if ((modifiers & ~SupportedModifiers) != 0)
        {
            throw new InvalidDataException($"Shortcut '{shortcut}' uses an unsupported modifier.");
        }

        return new ShortcutGesture(key, modifiers);
    }

    private static KeyModifiers ParseModifier(string modifierName, string shortcut) => modifierName.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => KeyModifiers.Control,
        "ALT" => KeyModifiers.Alt,
        "SHIFT" => KeyModifiers.Shift,
        "META" or "CMD" or "COMMAND" => KeyModifiers.Meta,
        _ => throw new InvalidDataException($"Shortcut '{shortcut}' contains unknown modifier '{modifierName}'."),
    };

    private readonly record struct ShortcutGesture(Key Key, KeyModifiers Modifiers);

    private sealed record ShortcutConfiguration(int Version, Dictionary<string, string> Shortcuts);
}
