using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Models;
using Serilog;

namespace BengaliKeyboard.Layouts;

/// <summary>
/// Loads and validates JSON keyboard layout definitions from disk with path-traversal protection,
/// strict schema/bounds verification, and automatic embedded-resource fallback (Rules.md #3, AS-03, CHK-06, CHK-1.9).
/// </summary>
public sealed class LayoutLoader
{
    public const int MaxLayoutFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _definitionsRootDirectory;

    public string DefinitionsRootDirectory => _definitionsRootDirectory;

    public LayoutLoader(string? customDefinitionsDirectory = null)
    {
        var baseDir = string.IsNullOrWhiteSpace(customDefinitionsDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Definitions")
            : customDefinitionsDirectory;

        _definitionsRootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir))
            + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Resolves and loads a layout file strictly within DefinitionsRootDirectory, validating all rows and keys.
    /// </summary>
    public async Task<KeyboardLayoutModel> LoadFromFileAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var candidateFullPath = Path.GetFullPath(Path.Combine(_definitionsRootDirectory, relativePath));
        if (!candidateFullPath.StartsWith(_definitionsRootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path traversal rejected: '{relativePath}' resolves outside Definitions directory.");
        }

        var fileInfo = new FileInfo(candidateFullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"Keyboard layout file not found: {candidateFullPath}", candidateFullPath);
        }

        if (fileInfo.Length > MaxLayoutFileBytes)
        {
            throw new InvalidDataException(
                $"Keyboard layout file '{relativePath}' exceeds maximum allowed size ({fileInfo.Length} bytes).");
        }

        await using var stream = new FileStream(
            candidateFullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await DeserializeAndValidateAsync(stream, relativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a layout from disk, or falls back to the assembly's embedded JSON resource if missing/corrupted (Rules.md #3).
    /// </summary>
    public async Task<KeyboardLayoutModel> LoadWithEmbeddedFallbackAsync(
        string relativePath,
        string embeddedResourceSuffix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await LoadFromFileAsync(relativePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not OperationCanceledException)
        {
            Log.Warning(
                ex,
                "Falling back to embedded resource '{ResourceSuffix}' after error loading '{RelativePath}'.",
                embeddedResourceSuffix,
                relativePath);

            return await LoadFromEmbeddedResourceAsync(embeddedResourceSuffix, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<KeyboardLayoutModel> LoadFromEmbeddedResourceAsync(
        string embeddedResourceSuffix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddedResourceSuffix);

        var assembly = typeof(LayoutLoader).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(embeddedResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded layout resource ending with '{embeddedResourceSuffix}' was not found.");

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Unable to open embedded layout stream '{resourceName}'.");

        return await DeserializeAndValidateAsync(stream, resourceName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the shifted English QWERTY layout from the primary English QWERTY layout by promoting AlternateOutput glyphs.
    /// </summary>
    public static KeyboardLayoutModel CreateShiftedEnglishLayout(KeyboardLayoutModel primaryQwerty)
    {
        ArgumentNullException.ThrowIfNull(primaryQwerty);

        var shiftedRows = new List<KeyboardRowModel>(primaryQwerty.Rows.Count);
        foreach (var row in primaryQwerty.Rows)
        {
            var shiftedKeys = new List<KeyDefinition>(row.Keys.Count);
            foreach (var key in row.Keys)
            {
                if (key.KeyType == KeyType.Shift)
                {
                    shiftedKeys.Add(new KeyDefinition
                    {
                        Id = key.Id + "_active",
                        DisplayLabel = "⬆",
                        UnicodeOutput = string.Empty,
                        KeyType = KeyType.Shift,
                        TargetLayer = LayoutLayer.Primary,
                        WidthRatio = key.WidthRatio,
                        RowIndex = key.RowIndex,
                        ColumnIndex = key.ColumnIndex,
                        Tooltip = "Return to lowercase"
                    });
                }
                else if (!string.IsNullOrEmpty(key.AlternateOutput))
                {
                    shiftedKeys.Add(new KeyDefinition
                    {
                        Id = key.Id + "_shift",
                        DisplayLabel = key.AlternateOutput,
                        UnicodeOutput = key.AlternateOutput,
                        AlternateOutput = key.DisplayLabel,
                        KeyType = key.KeyType,
                        WidthRatio = key.WidthRatio,
                        RowIndex = key.RowIndex,
                        ColumnIndex = key.ColumnIndex,
                        Tooltip = key.Tooltip
                    });
                }
                else
                {
                    shiftedKeys.Add(key);
                }
            }

            shiftedRows.Add(new KeyboardRowModel
            {
                RowIndex = row.RowIndex,
                HeightRatio = row.HeightRatio,
                Keys = shiftedKeys
            });
        }

        return new KeyboardLayoutModel
        {
            Id = "English.Shift",
            DisplayName = "English (QWERTY Shift)",
            Language = LanguageMode.English,
            Layer = LayoutLayer.Shift,
            Rows = shiftedRows
        };
    }

    public static void ValidateLayoutModel(KeyboardLayoutModel? layout, string sourceIdentifier)
    {
        if (layout is null)
        {
            throw new InvalidDataException($"Layout deserialized as null from '{sourceIdentifier}'.");
        }

        if (string.IsNullOrWhiteSpace(layout.Id))
        {
            throw new InvalidDataException($"Layout in '{sourceIdentifier}' is missing a valid Id.");
        }

        if (layout.Rows is null || layout.Rows.Count == 0)
        {
            throw new InvalidDataException($"Layout '{layout.Id}' in '{sourceIdentifier}' contains zero rows.");
        }

        foreach (var row in layout.Rows)
        {
            if (double.IsNaN(row.HeightRatio) || double.IsInfinity(row.HeightRatio) ||
                row.HeightRatio < 0.4 || row.HeightRatio > 2.5)
            {
                throw new InvalidDataException(
                    $"Row {row.RowIndex} in layout '{layout.Id}' has invalid HeightRatio ({row.HeightRatio}).");
            }

            if (row.Keys is null || row.Keys.Count == 0)
            {
                throw new InvalidDataException($"Row {row.RowIndex} in layout '{layout.Id}' contains zero keys.");
            }

            foreach (var key in row.Keys)
            {
                if (!key.IsValid())
                {
                    throw new InvalidDataException(
                        $"Invalid key definition '{key.Id}' in layout '{layout.Id}' (Row {row.RowIndex}).");
                }
            }
        }
    }

    private static async Task<KeyboardLayoutModel> DeserializeAndValidateAsync(
        Stream stream,
        string sourceIdentifier,
        CancellationToken cancellationToken)
    {
        KeyboardLayoutModel? layout;
        try
        {
            layout = await JsonSerializer
                .DeserializeAsync<KeyboardLayoutModel>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed JSON in keyboard layout '{sourceIdentifier}'.", ex);
        }

        ValidateLayoutModel(layout, sourceIdentifier);
        return layout!;
    }
}
