using BengaliKeyboard.Storage.Security;
using Microsoft.Data.Sqlite;
using Serilog;

namespace BengaliKeyboard.Storage.Database;

/// <summary>
/// Initializes and verifies SQLite schemas for keyboard.db and dictionary.db,
/// recovers from database file corruption, and ensures the per-user DPAPI HMAC key exists.
/// </summary>
public sealed class DatabaseInitializer
{
    public const string ClipboardHmacSettingKey = "ClipboardHmacKey_DPAPI";
    private const int SqliteCorruptErrorCode = 11;
    private const int SqliteNotADatabaseErrorCode = 26;

    private readonly AppPaths _appPaths;
    private readonly SqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(AppPaths appPaths, SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _appPaths = appPaths;
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Ensures both keyboard.db and dictionary.db schemas and indexes exist and are healthy.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _appPaths.EnsureDirectoriesCreated();

        await InitializeDatabaseWithCorruptionRecoveryAsync(
            _appPaths.UserDatabasePath,
            InitializeUserDatabaseCoreAsync,
            cancellationToken).ConfigureAwait(false);

        await InitializeDatabaseWithCorruptionRecoveryAsync(
            _appPaths.DictionaryDatabasePath,
            InitializeDictionaryDatabaseCoreAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves (or creates if missing) the DPAPI-protected per-user HMAC-SHA256 key stored in ApplicationSettings.
    /// </summary>
    public async Task<string> GetOrCreateClipboardHmacKeyAsync(CancellationToken cancellationToken = default)
    {
        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var selectCmd = connection.CreateCommand())
                {
                    selectCmd.CommandText = "SELECT Value FROM ApplicationSettings WHERE Key = @key LIMIT 1;";
                    selectCmd.Parameters.AddWithValue("@key", ClipboardHmacSettingKey);
                    var existing = await selectCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        return existing;
                    }
                }

                var newProtectedKey = LocalDataProtector.GenerateProtectedHmacKey();
                await using (var upsertCmd = connection.CreateCommand())
                {
                    upsertCmd.CommandText = """
                        INSERT INTO ApplicationSettings (Key, Value)
                        VALUES (@key, @value)
                        ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                        """;
                    upsertCmd.Parameters.AddWithValue("@key", ClipboardHmacSettingKey);
                    upsertCmd.Parameters.AddWithValue("@value", newProtectedKey);
                    await upsertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                return newProtectedKey;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeUserDatabaseCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .CreateUserDatabaseConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS EmojiUsage (
                Emoji TEXT PRIMARY KEY,
                UsageCount INTEGER NOT NULL DEFAULT 1,
                LastUsedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EmojiUsage_Rank
                ON EmojiUsage (UsageCount DESC, LastUsedUtc DESC);

            CREATE TABLE IF NOT EXISTS ClipboardHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                ContentHash TEXT NOT NULL UNIQUE,
                CreatedUtc TEXT NOT NULL,
                IsPinned INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_ClipboardHistory_PinnedCreated
                ON ClipboardHistory (IsPinned DESC, CreatedUtc DESC);

            CREATE TABLE IF NOT EXISTS ApplicationSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS UserDictionary (
                Word TEXT PRIMARY KEY,
                Frequency INTEGER NOT NULL DEFAULT 1,
                LastUsedUtc TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _ = await GetOrCreateClipboardHmacKeyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeDictionaryDatabaseCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .CreateDictionaryConnectionAsync(readOnly: false, cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS BengaliWords (
                Word TEXT PRIMARY KEY,
                Prefix2 TEXT NOT NULL,
                Frequency INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_BengaliWords_Prefix
                ON BengaliWords (Prefix2, Frequency DESC);

            CREATE TABLE IF NOT EXISTS Bigrams (
                Word1 TEXT NOT NULL,
                Word2 TEXT NOT NULL,
                Frequency INTEGER NOT NULL,
                PRIMARY KEY (Word1, Word2)
            );
            CREATE INDEX IF NOT EXISTS IX_Bigrams_Lookup
                ON Bigrams (Word1, Frequency DESC);

            CREATE TABLE IF NOT EXISTS Trigrams (
                Word1 TEXT NOT NULL,
                Word2 TEXT NOT NULL,
                Word3 TEXT NOT NULL,
                Frequency INTEGER NOT NULL,
                PRIMARY KEY (Word1, Word2, Word3)
            );
            CREATE INDEX IF NOT EXISTS IX_Trigrams_Lookup
                ON Trigrams (Word1, Word2, Frequency DESC);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InitializeDatabaseWithCorruptionRecoveryAsync(
        string databaseFilePath,
        Func<CancellationToken, Task> initializer,
        CancellationToken cancellationToken)
    {
        try
        {
            await initializer(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (
            ex.SqliteErrorCode == SqliteCorruptErrorCode ||
            ex.SqliteErrorCode == SqliteNotADatabaseErrorCode)
        {
            SqliteConnection.ClearAllPools();
            var backupPath = $"{databaseFilePath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            Log.Error(
                ex,
                "Detected corrupted SQLite database at {DatabaseFilePath}. Backing up to {BackupPath} and reinitializing.",
                databaseFilePath,
                backupPath);

            TryMoveOrDeleteFile(databaseFilePath, backupPath);
            TryMoveOrDeleteFile($"{databaseFilePath}-wal", $"{backupPath}-wal");
            TryMoveOrDeleteFile($"{databaseFilePath}-shm", $"{backupPath}-shm");

            await initializer(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryMoveOrDeleteFile(string sourcePath, string backupPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            File.Move(sourcePath, backupPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(sourcePath);
            }
            catch
            {
                // Ignore if locked
            }
        }
    }
}
