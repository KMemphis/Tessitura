using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Tessitura.IO.Tess;

/// <summary>Upgrades the score JSON of one format version to the next.</summary>
/// <param name="FromVersion">The version this step reads; it produces <c>FromVersion + 1</c>.</param>
/// <param name="Upgrade">Rewrites the score document in place.</param>
public sealed record TessMigration(int FromVersion, Action<JsonObject> Upgrade);

/// <summary>Applies the chain of migrations that brings any older archive to the current format.</summary>
public sealed class TessMigrator
{
    /// <summary>The format version written by this build.</summary>
    public const int CurrentVersion = 1;

    private readonly ImmutableArray<TessMigration> _migrations;

    /// <summary>Creates a migrator over the built-in migrations (none exist before version 1).</summary>
    public TessMigrator() : this(CurrentVersion, []) { }

    /// <summary>Creates a migrator with an explicit target version and steps.</summary>
    /// <param name="currentVersion">The version migrations lead to.</param>
    /// <param name="migrations">The steps, one per version below <paramref name="currentVersion"/> that needs one.</param>
    public TessMigrator(int currentVersion, ImmutableArray<TessMigration> migrations)
    {
        TargetVersion = currentVersion;
        _migrations = migrations;
    }

    /// <summary>Gets the version this migrator upgrades documents to.</summary>
    public int TargetVersion { get; }

    /// <summary>Upgrades a score document from its stored version to the target version.</summary>
    /// <param name="score">The parsed score.json.</param>
    /// <param name="fromVersion">The version recorded in the manifest.</param>
    /// <exception cref="InvalidDataException">The archive is newer than this build or a step is missing.</exception>
    public void Migrate(JsonObject score, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (fromVersion > TargetVersion)
        {
            throw new InvalidDataException(
                $"The file uses .tess format {fromVersion}, newer than the supported {TargetVersion}.");
        }

        for (int version = fromVersion; version < TargetVersion; version++)
        {
            TessMigration? step = null;
            foreach (TessMigration candidate in _migrations)
            {
                if (candidate.FromVersion == version)
                {
                    step = candidate;
                }
            }

            if (step is null)
            {
                throw new InvalidDataException($"No migration exists from .tess format {version}.");
            }

            step.Upgrade(score);
        }
    }
}
