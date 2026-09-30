using Avalonia.Input;
using System.Text.Json;
using Tessitura.App;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ActionRegistryTests
{
    [Fact]
    public void CreatesDefaultJsonAndExecutesItsConfiguredGesture()
    {
        string settingsPath = TemporarySettingsPath();
        int executions = 0;
        ActionDefinition[] actions =
        [
            new("score.undo", "Undo", "Ctrl+K", () => executions++),
        ];

        try
        {
            ActionRegistry registry = ActionRegistry.LoadOrCreate(actions, settingsPath);

            Assert.True(File.Exists(settingsPath));
            using JsonDocument configuration = JsonDocument.Parse(File.ReadAllText(settingsPath));
            Assert.Equal("Ctrl+K", configuration.RootElement.GetProperty("shortcuts")
                .GetProperty("score.undo").GetString());
            Assert.False(registry.TryExecute(Key.K, KeyModifiers.None));
            Assert.True(registry.TryExecute(Key.K, KeyModifiers.Control));
            Assert.Equal(1, executions);
            Assert.True(registry.TryExecute("score.undo"));
            Assert.Equal(2, executions);
            Assert.Equal(new RegisteredAction("score.undo", "Undo", "Ctrl+K"),
                Assert.Single(registry.Actions));
        }
        finally
        {
            DeleteSettingsFile(settingsPath);
        }
    }

    [Fact]
    public void ChangingJsonBindingChangesWhichKeyExecutesTheAction()
    {
        string settingsPath = TemporarySettingsPath();
        int executions = 0;
        ActionDefinition[] actions =
        [
            new("score.undo", "Undo", "Ctrl+K", () => executions++),
        ];

        try
        {
            File.WriteAllText(settingsPath,
                """{"version":1,"shortcuts":{"score.undo":"Ctrl+J"}}""");

            ActionRegistry registry = ActionRegistry.LoadOrCreate(actions, settingsPath);

            Assert.False(registry.TryExecute(Key.K, KeyModifiers.Control));
            Assert.True(registry.TryExecute(Key.J, KeyModifiers.Control));
            Assert.Equal(1, executions);
            Assert.Equal("Ctrl+J", Assert.Single(registry.Actions).Shortcut);
        }
        finally
        {
            DeleteSettingsFile(settingsPath);
        }
    }

    [Fact]
    public void RejectsConflictingShortcutsInsteadOfDispatchingAmbiguously()
    {
        string settingsPath = TemporarySettingsPath();
        ActionDefinition[] actions =
        [
            new("score.undo", "Undo", "Ctrl+K", static () => { }),
            new("score.redo", "Redo", "Ctrl+J", static () => { }),
        ];

        try
        {
            File.WriteAllText(settingsPath,
                """{"version":1,"shortcuts":{"score.undo":"Ctrl+K","score.redo":"Ctrl+K"}}""");

            Assert.Throws<InvalidOperationException>(() =>
                ActionRegistry.LoadOrCreate(actions, settingsPath));
        }
        finally
        {
            DeleteSettingsFile(settingsPath);
        }
    }

    [Fact]
    public void DoesNotWriteDefaultConfigurationWhenDefaultShortcutsConflict()
    {
        string settingsPath = TemporarySettingsPath();
        ActionDefinition[] actions =
        [
            new("score.undo", "Undo", "Ctrl+K", static () => { }),
            new("score.redo", "Redo", "Ctrl+K", static () => { }),
        ];

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                ActionRegistry.LoadOrCreate(actions, settingsPath));
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            DeleteSettingsFile(settingsPath);
        }
    }

    [Fact]
    public void RejectsUnknownActionIdsAndMalformedShortcutsInJson()
    {
        string settingsPath = TemporarySettingsPath();
        ActionDefinition[] actions =
        [
            new("score.undo", "Undo", "Ctrl+K", static () => { }),
        ];

        try
        {
            File.WriteAllText(settingsPath,
                """{"version":1,"shortcuts":{"score.unknown":"Ctrl+J"}}""");
            Assert.Throws<InvalidDataException>(() =>
                ActionRegistry.LoadOrCreate(actions, settingsPath));

            File.WriteAllText(settingsPath,
                """{"version":1,"shortcuts":{"score.undo":"Ctrl+NotAKey"}}""");
            Assert.Throws<InvalidDataException>(() =>
                ActionRegistry.LoadOrCreate(actions, settingsPath));
        }
        finally
        {
            DeleteSettingsFile(settingsPath);
        }
    }

    private static string TemporarySettingsPath() => Path.Combine(
        Path.GetTempPath(), $"tessitura-shortcuts-{Guid.NewGuid():N}.json");

    private static void DeleteSettingsFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
