using System.Globalization;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.Storage.Security;
using Serilog;

namespace BengaliKeyboard.Clipboard;

/// <summary>
/// Persists and queries user clipboard entries in <c>keyboard.db</c> (<c>ClipboardHistory</c> table).
/// Encrypts <c>Content</c> at rest using Windows DPAPI (<see cref="LocalDataProtector.ProtectString"/>)
/// and computes <c>ContentHash</c> using a per-user DPAPI-protected 256-bit <c>HMACSHA256</c> key (SEC-01, SEC-02, CHK-01).
/// </summary>
public sealed class ClipboardRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseInitializer _databaseInitializer;

    public ClipboardRepository(
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializer databaseInitializer)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(databaseInitializer);

        _connectionFactory = connectionFactory;
        _databaseInitializer = databaseInitializer;
    }

    /// <summary>
    /// Inserts or deduplicates a clipboard text entry, enforces retention and capacity limits,
    /// and returns the persisted <see cref="ClipboardEntry"/>.
    /// </summary>
    public async Task<ClipboardEntry?> UpsertEntryAsync(
        string plaintextContent,
        int maxUnpinnedLimit = 100,
        int retentionDays = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextContent) ||
            plaintextContent.Length > ClipboardEntry.MaxAllowedContentCharacters)
        {
            return null;
        }

        var clampedLimit = Math.Clamp(maxUnpinnedLimit, 10, 500);
        var clampedRetentionDays = Math.Clamp(retentionDays, 1, 365);

        var protectedHmacKey = await _databaseInitializer
            .GetOrCreateClipboardHmacKeyAsync(cancellationToken)
            .ConfigureAwait(false);

        var contentHash = LocalDataProtector.ComputeKeyedHash(plaintextContent, protectedHmacKey);
        var encryptedContent = LocalDataProtector.ProtectString(plaintextContent);
        var nowUtc = DateTimeOffset.UtcNow;
        var nowIso = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        var cutoffIso = nowUtc.AddDays(-clampedRetentionDays).ToString("O", CultureInfo.InvariantCulture);

        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                // 1. Upsert by keyed ContentHash (preserving existing IsPinned state on duplicate copy)
                await using (var upsertCmd = connection.CreateCommand())
                {
                    upsertCmd.CommandText = """
                        INSERT INTO ClipboardHistory (Content, ContentHash, CreatedUtc, IsPinned)
                        VALUES (@content, @contentHash, @createdUtc, 0)
                        ON CONFLICT(ContentHash) DO UPDATE SET
                            Content = excluded.Content,
                            CreatedUtc = excluded.CreatedUtc;
                        """;
                    upsertCmd.Parameters.AddWithValue("@content", encryptedContent);
                    upsertCmd.Parameters.AddWithValue("@contentHash", contentHash);
                    upsertCmd.Parameters.AddWithValue("@createdUtc", nowIso);
                    await upsertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // 2. Prune expired unpinned entries older than retention period
                await using (var expireCmd = connection.CreateCommand())
                {
                    expireCmd.CommandText = """
                        DELETE FROM ClipboardHistory
                        WHERE IsPinned = 0 AND CreatedUtc < @cutoffUtc;
                        """;
                    expireCmd.Parameters.AddWithValue("@cutoffUtc", cutoffIso);
                    await expireCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // 3. Prune excess unpinned entries beyond configured capacity limit
                await using (var capCmd = connection.CreateCommand())
                {
                    capCmd.CommandText = """
                        DELETE FROM ClipboardHistory
                        WHERE IsPinned = 0
                          AND Id NOT IN (
                              SELECT Id
                              FROM ClipboardHistory
                              WHERE IsPinned = 0
                              ORDER BY CreatedUtc DESC, Id DESC
                              LIMIT @maxLimit
                          );
                        """;
                    capCmd.Parameters.AddWithValue("@maxLimit", clampedLimit);
                    await capCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // 4. Read back Id and IsPinned of the upserted record
                await using var selectCmd = connection.CreateCommand();
                selectCmd.CommandText = """
                    SELECT Id, CreatedUtc, IsPinned
                    FROM ClipboardHistory
                    WHERE ContentHash = @contentHash
                    LIMIT 1;
                    """;
                selectCmd.Parameters.AddWithValue("@contentHash", contentHash);

                await using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var id = reader.GetInt64(0);
                    var createdRaw = reader.GetString(1);
                    var isPinned = reader.GetInt32(2) != 0;

                    var parsedCreated = DateTimeOffset.TryParse(
                        createdRaw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var dto)
                        ? dto
                        : nowUtc;

                    return new ClipboardEntry
                    {
                        Id = id,
                        Content = plaintextContent,
                        ContentHash = contentHash,
                        CreatedUtc = parsedCreated,
                        IsPinned = isPinned
                    };
                }

                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves clipboard entries ordered with Pinned items at the top (<c>IsPinned DESC, CreatedUtc DESC</c>),
    /// decrypting DPAPI-protected content in memory.
    /// </summary>
    public async Task<IReadOnlyList<ClipboardEntry>> GetEntriesAsync(
        string? searchQuery = null,
        int limit = 100,
        int retentionDays = 30,
        CancellationToken cancellationToken = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 500);
        var clampedRetentionDays = Math.Clamp(retentionDays, 1, 365);
        var cutoffIso = DateTimeOffset.UtcNow.AddDays(-clampedRetentionDays).ToString("O", CultureInfo.InvariantCulture);
        var normalizedQuery = searchQuery?.Trim();

        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                // Clean up any expired unpinned entries before reading
                await using (var expireCmd = connection.CreateCommand())
                {
                    expireCmd.CommandText = """
                        DELETE FROM ClipboardHistory
                        WHERE IsPinned = 0 AND CreatedUtc < @cutoffUtc;
                        """;
                    expireCmd.Parameters.AddWithValue("@cutoffUtc", cutoffIso);
                    await expireCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var selectCmd = connection.CreateCommand();
                selectCmd.CommandText = """
                    SELECT Id, Content, ContentHash, CreatedUtc, IsPinned
                    FROM ClipboardHistory
                    ORDER BY IsPinned DESC, CreatedUtc DESC, Id DESC;
                    """;

                var results = new List<ClipboardEntry>();
                await using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var id = reader.GetInt64(0);
                    var encryptedOrRaw = reader.GetString(1);
                    var hash = reader.GetString(2);
                    var createdRaw = reader.GetString(3);
                    var isPinned = reader.GetInt32(4) != 0;

                    var decryptedContent = TryDecryptContent(encryptedOrRaw);
                    if (string.IsNullOrEmpty(decryptedContent))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(normalizedQuery) &&
                        !decryptedContent.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var createdUtc = DateTimeOffset.TryParse(
                        createdRaw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var dto)
                        ? dto
                        : DateTimeOffset.UtcNow;

                    results.Add(new ClipboardEntry
                    {
                        Id = id,
                        Content = decryptedContent,
                        ContentHash = hash,
                        CreatedUtc = createdUtc,
                        IsPinned = isPinned
                    });

                    if (results.Count >= clampedLimit)
                    {
                        break;
                    }
                }

                return (IReadOnlyList<ClipboardEntry>)results;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPinnedAsync(
        long id,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE ClipboardHistory
                    SET IsPinned = @isPinned
                    WHERE Id = @id;
                    """;
                command.Parameters.AddWithValue("@isPinned", isPinned ? 1 : 0);
                command.Parameters.AddWithValue("@id", id);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteEntryAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.CommandText = "DELETE FROM ClipboardHistory WHERE Id = @id;";
                    deleteCmd.Parameters.AddWithValue("@id", id);
                    await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await CheckpointWalAsync(connection, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearUnpinnedAsync(CancellationToken cancellationToken = default)
    {
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.CommandText = "DELETE FROM ClipboardHistory WHERE IsPinned = 0;";
                    await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await CheckpointWalAsync(connection, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.CommandText = "DELETE FROM ClipboardHistory;";
                    await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await CheckpointWalAsync(connection, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task CheckpointWalAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var checkpointCmd = connection.CreateCommand();
        checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await checkpointCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string TryDecryptContent(string storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return string.Empty;
        }

        try
        {
            return LocalDataProtector.UnprotectString(storedValue);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to decrypt clipboard entry payload; skipping unreadable entry.");
            return string.Empty;
        }
    }
}
