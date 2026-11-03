using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BengaliKeyboard.Storage.Database;
using Serilog;

namespace BengaliKeyboard.Prediction;

/// <summary>
/// Manages the offline Bengali dictionary (<c>dictionary.db</c>) and user-learned vocabulary (<c>UserDictionary</c> in <c>keyboard.db</c>).
/// Seeds <c>BengaliWords</c>, <c>Bigrams</c>, and <c>Trigrams</c> from <c>data/Dictionary/bn_dictionary_seed.json</c>
/// (with embedded resource fallback per Rules.md Scenario 5) and queries <c>dictionary.db</c> in <c>ReadOnly</c> mode at runtime (CHK-10).
/// </summary>
public sealed class DictionaryService
{
    public const long MaxSeedFileBytes = 4 * 1024 * 1024; // 4 MB cap
    private const string EmbeddedSeedResourceName = "BengaliKeyboard.Prediction.data.Dictionary.bn_dictionary_seed.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16
    };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly string? _customSeedFilePath;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _isSeeded;

    public DictionaryService(SqliteConnectionFactory connectionFactory, string? customSeedFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
        _customSeedFilePath = customSeedFilePath;
    }

    /// <summary>
    /// Ensures <c>dictionary.db</c> is populated from <c>bn_dictionary_seed.json</c>.
    /// Safe to call multiple times; executes only once per process lifetime.
    /// </summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (_isSeeded)
        {
            return;
        }

        await _seedLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isSeeded)
            {
                return;
            }

            await SqliteConnectionFactory.ExecuteWithRetryAsync(
                async ct =>
                {
                    await using var connection = await _connectionFactory
                        .CreateDictionaryConnectionAsync(readOnly: false, ct)
                        .ConfigureAwait(false);

                    await using (var countCmd = connection.CreateCommand())
                    {
                        countCmd.CommandText = "SELECT COUNT(1) FROM BengaliWords;";
                        var existingCount = Convert.ToInt64(
                            await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false),
                            CultureInfo.InvariantCulture);

                        if (existingCount > 0)
                        {
                            _isSeeded = true;
                            return;
                        }
                    }

                    var seedPayload = LoadSeedPayload();
                    await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
                        .BeginTransactionAsync(ct)
                        .ConfigureAwait(false);

                    if (seedPayload.Words is { Count: > 0 })
                    {
                        await using var wordCmd = connection.CreateCommand();
                        wordCmd.Transaction = transaction;
                        wordCmd.CommandText = """
                            INSERT INTO BengaliWords (Word, Prefix2, Frequency)
                            VALUES (@word, @prefix2, @frequency)
                            ON CONFLICT(Word) DO UPDATE SET Frequency = excluded.Frequency;
                            """;

                        var pWord = wordCmd.Parameters.Add("@word", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pPrefix2 = wordCmd.Parameters.Add("@prefix2", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pFreq = wordCmd.Parameters.Add("@frequency", Microsoft.Data.Sqlite.SqliteType.Integer);

                        foreach (var entry in seedPayload.Words)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Word))
                            {
                                continue;
                            }

                            var normalized = entry.Word.Trim().Normalize(NormalizationForm.FormC);
                            pWord.Value = normalized;
                            pPrefix2.Value = ExtractPrefix2Key(normalized);
                            pFreq.Value = Math.Max(1, entry.Frequency);
                            await wordCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                    }

                    if (seedPayload.Bigrams is { Count: > 0 })
                    {
                        await using var bigramCmd = connection.CreateCommand();
                        bigramCmd.Transaction = transaction;
                        bigramCmd.CommandText = """
                            INSERT INTO Bigrams (Word1, Word2, Frequency)
                            VALUES (@w1, @w2, @frequency)
                            ON CONFLICT(Word1, Word2) DO UPDATE SET Frequency = excluded.Frequency;
                            """;

                        var pW1 = bigramCmd.Parameters.Add("@w1", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pW2 = bigramCmd.Parameters.Add("@w2", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pFreq = bigramCmd.Parameters.Add("@frequency", Microsoft.Data.Sqlite.SqliteType.Integer);

                        foreach (var entry in seedPayload.Bigrams)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Word1) || string.IsNullOrWhiteSpace(entry.Word2))
                            {
                                continue;
                            }

                            pW1.Value = entry.Word1.Trim().Normalize(NormalizationForm.FormC);
                            pW2.Value = entry.Word2.Trim().Normalize(NormalizationForm.FormC);
                            pFreq.Value = Math.Max(1, entry.Frequency);
                            await bigramCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                    }

                    if (seedPayload.Trigrams is { Count: > 0 })
                    {
                        await using var trigramCmd = connection.CreateCommand();
                        trigramCmd.Transaction = transaction;
                        trigramCmd.CommandText = """
                            INSERT INTO Trigrams (Word1, Word2, Word3, Frequency)
                            VALUES (@w1, @w2, @w3, @frequency)
                            ON CONFLICT(Word1, Word2, Word3) DO UPDATE SET Frequency = excluded.Frequency;
                            """;

                        var pW1 = trigramCmd.Parameters.Add("@w1", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pW2 = trigramCmd.Parameters.Add("@w2", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pW3 = trigramCmd.Parameters.Add("@w3", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pFreq = trigramCmd.Parameters.Add("@frequency", Microsoft.Data.Sqlite.SqliteType.Integer);

                        foreach (var entry in seedPayload.Trigrams)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Word1) ||
                                string.IsNullOrWhiteSpace(entry.Word2) ||
                                string.IsNullOrWhiteSpace(entry.Word3))
                            {
                                continue;
                            }

                            pW1.Value = entry.Word1.Trim().Normalize(NormalizationForm.FormC);
                            pW2.Value = entry.Word2.Trim().Normalize(NormalizationForm.FormC);
                            pW3.Value = entry.Word3.Trim().Normalize(NormalizationForm.FormC);
                            pFreq.Value = Math.Max(1, entry.Frequency);
                            await trigramCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                    }

                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    _isSeeded = true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _seedLock.Release();
        }
    }

    /// <summary>
    /// Queries both the read-only static <c>BengaliWords</c> table and the mutable <c>UserDictionary</c> table
    /// for words starting with <paramref name="prefix"/>.
    /// </summary>
    public async Task<IReadOnlyList<(string Word, int Frequency, bool IsUserWord)>> QueryPrefixMatchesAsync(
        string prefix,
        int maxCandidates = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return [];
        }

        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var normalizedPrefix = prefix.Trim().Normalize(NormalizationForm.FormC);
        var escapedLikePattern = EscapeSqliteLikePattern(normalizedPrefix) + "%";
        var clampedMax = Math.Clamp(maxCandidates, 1, 50);

        var combined = new Dictionary<string, (int Frequency, bool IsUserWord)>(StringComparer.Ordinal);

        // 1. Query UserDictionary in keyboard.db
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var userConn = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using var userCmd = userConn.CreateCommand();
                userCmd.CommandText = """
                    SELECT Word, Frequency
                    FROM UserDictionary
                    WHERE Word LIKE @pattern ESCAPE '\'
                    ORDER BY Frequency DESC, LastUsedUtc DESC
                    LIMIT @limit;
                    """;
                userCmd.Parameters.AddWithValue("@pattern", escapedLikePattern);
                userCmd.Parameters.AddWithValue("@limit", clampedMax);

                await using var reader = await userCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var word = reader.GetString(0);
                    var freq = reader.GetInt32(1);
                    combined[word] = (Math.Max(1, freq), true);
                }
            },
            cancellationToken).ConfigureAwait(false);

        // 2. Query BengaliWords in dictionary.db (ReadOnly mode per CHK-10)
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var dictConn = await _connectionFactory
                    .CreateDictionaryConnectionAsync(readOnly: true, ct)
                    .ConfigureAwait(false);

                await using var dictCmd = dictConn.CreateCommand();
                if (normalizedPrefix.Length >= 2)
                {
                    dictCmd.CommandText = """
                        SELECT Word, Frequency
                        FROM BengaliWords
                        WHERE Prefix2 = @prefix2 AND Word LIKE @pattern ESCAPE '\'
                        ORDER BY Frequency DESC
                        LIMIT @limit;
                        """;
                    dictCmd.Parameters.AddWithValue("@prefix2", ExtractPrefix2Key(normalizedPrefix));
                }
                else
                {
                    dictCmd.CommandText = """
                        SELECT Word, Frequency
                        FROM BengaliWords
                        WHERE Word LIKE @pattern ESCAPE '\'
                        ORDER BY Frequency DESC
                        LIMIT @limit;
                        """;
                }

                dictCmd.Parameters.AddWithValue("@pattern", escapedLikePattern);
                dictCmd.Parameters.AddWithValue("@limit", clampedMax);

                await using var reader = await dictCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var word = reader.GetString(0);
                    var freq = reader.GetInt32(1);
                    if (combined.TryGetValue(word, out var existing))
                    {
                        combined[word] = (existing.Frequency + freq, existing.IsUserWord);
                    }
                    else
                    {
                        combined[word] = (freq, false);
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

        return combined
            .Select(kvp => (Word: kvp.Key, kvp.Value.Frequency, kvp.Value.IsUserWord))
            .OrderByDescending(x => x.IsUserWord ? x.Frequency + 500 : x.Frequency)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.Ordinal)
            .Take(clampedMax)
            .ToList();
    }

    public async Task RecordUserWordAsync(string word, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        var normalized = word.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length < 2 || normalized.Length > 64)
        {
            return;
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO UserDictionary (Word, Frequency, LastUsedUtc)
                    VALUES (@word, 1, @lastUsedUtc)
                    ON CONFLICT(Word) DO UPDATE SET
                        Frequency = UserDictionary.Frequency + 1,
                        LastUsedUtc = excluded.LastUsedUtc;
                    """;
                cmd.Parameters.AddWithValue("@word", normalized);
                cmd.Parameters.AddWithValue("@lastUsedUtc", nowIso);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearUserDictionaryAsync(CancellationToken cancellationToken = default)
    {
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.CommandText = "DELETE FROM UserDictionary;";
                    await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var checkpointCmd = connection.CreateCommand())
                {
                    checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    await checkpointCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static string ExtractPrefix2Key(string normalizedWord)
    {
        if (string.IsNullOrEmpty(normalizedWord))
        {
            return string.Empty;
        }

        return normalizedWord.Length >= 2 ? normalizedWord[..2] : normalizedWord;
    }

    /// <summary>
    /// Escapes SQLite <c>LIKE</c> wildcard characters (<c>%</c>, <c>_</c>, <c>\</c>) per security-checklist.md section 1.5.
    /// </summary>
    public static string EscapeSqliteLikePattern(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        return raw
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private DictionarySeedPayload LoadSeedPayload()
    {
        foreach (var path in GetCandidateSeedPaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxSeedFileBytes)
                {
                    continue;
                }

                var json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<DictionarySeedPayload>(json, SerializerOptions);
                if (parsed is { Words.Count: > 0 })
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read Bengali dictionary seed at {SeedPath}; falling back to embedded seed.", path);
            }
        }

        return LoadEmbeddedSeedPayload();
    }

    private IEnumerable<string> GetCandidateSeedPaths()
    {
        if (!string.IsNullOrWhiteSpace(_customSeedFilePath))
        {
            yield return _customSeedFilePath;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "data", "Dictionary", "bn_dictionary_seed.json");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "Dictionary", "bn_dictionary_seed.json");
    }

    private static DictionarySeedPayload LoadEmbeddedSeedPayload()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(EmbeddedSeedResourceName);
            if (stream is null)
            {
                return new DictionarySeedPayload();
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<DictionarySeedPayload>(json, SerializerOptions) ?? new DictionarySeedPayload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load embedded Bengali dictionary seed.");
            return new DictionarySeedPayload();
        }
    }

    private sealed class DictionarySeedPayload
    {
        public List<SeedWordEntry> Words { get; set; } = [];

        public List<SeedBigramEntry> Bigrams { get; set; } = [];

        public List<SeedTrigramEntry> Trigrams { get; set; } = [];
    }

    private sealed class SeedWordEntry
    {
        public string Word { get; set; } = string.Empty;

        public int Frequency { get; set; }
    }

    private sealed class SeedBigramEntry
    {
        public string Word1 { get; set; } = string.Empty;

        public string Word2 { get; set; } = string.Empty;

        public int Frequency { get; set; }
    }

    private sealed class SeedTrigramEntry
    {
        public string Word1 { get; set; } = string.Empty;

        public string Word2 { get; set; } = string.Empty;

        public string Word3 { get; set; } = string.Empty;

        public int Frequency { get; set; }
    }
}
