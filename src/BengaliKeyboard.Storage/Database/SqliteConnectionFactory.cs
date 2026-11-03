using Microsoft.Data.Sqlite;
using Serilog;

namespace BengaliKeyboard.Storage.Database;

/// <summary>
/// Creates and configures SQLite connections for keyboard.db and dictionary.db
/// with WAL journaling, secure_delete, and transient lock retry (Rules.md #4, CHK-1.10).
/// </summary>
public sealed class SqliteConnectionFactory
{
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;
    private const int MaxTransientRetries = 3;

    private readonly AppPaths _appPaths;

    public SqliteConnectionFactory(AppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _appPaths = appPaths;
    }

    /// <summary>
    /// Opens a connection to the user database (%LOCALAPPDATA%\BengaliKeyboard\Database\keyboard.db)
    /// with WAL mode and secure_delete enabled.
    /// </summary>
    public async Task<SqliteConnection> CreateUserDatabaseConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        _appPaths.EnsureDirectoriesCreated();

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.UserDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5,
            Pooling = true
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApplyConnectionPragmasAsync(connection, isReadOnly: false, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Opens a connection to the offline dictionary database (%LOCALAPPDATA%\BengaliKeyboard\Database\dictionary.db).
    /// Defaults to ReadOnly for normal prediction lookups (CHK-1.10).
    /// </summary>
    public async Task<SqliteConnection> CreateDictionaryConnectionAsync(
        bool readOnly = true,
        CancellationToken cancellationToken = default)
    {
        _appPaths.EnsureDirectoriesCreated();

        var openMode = readOnly && File.Exists(_appPaths.DictionaryDatabasePath)
            ? SqliteOpenMode.ReadOnly
            : SqliteOpenMode.ReadWriteCreate;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.DictionaryDatabasePath,
            Mode = openMode,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5,
            Pooling = true
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApplyConnectionPragmasAsync(
            connection,
            isReadOnly: openMode == SqliteOpenMode.ReadOnly,
            cancellationToken).ConfigureAwait(false);

        return connection;
    }

    /// <summary>
    /// Executes an asynchronous database operation with bounded exponential backoff on SQLITE_BUSY / SQLITE_LOCKED.
    /// </summary>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (
                attempt <= MaxTransientRetries &&
                (ex.SqliteErrorCode == SqliteBusyErrorCode || ex.SqliteErrorCode == SqliteLockedErrorCode))
            {
                var delayMs = 25 * (1 << (attempt - 1));
                Log.Warning(
                    "Transient SQLite lock (code {SqliteErrorCode}) on attempt {Attempt}/{MaxRetries}. Retrying in {DelayMs}ms.",
                    ex.SqliteErrorCode,
                    attempt,
                    MaxTransientRetries,
                    delayMs);

                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes an asynchronous database command with bounded exponential backoff on SQLITE_BUSY / SQLITE_LOCKED.
    /// </summary>
    public static Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    private static async Task ApplyConnectionPragmasAsync(
        SqliteConnection connection,
        bool isReadOnly,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = isReadOnly
            ? "PRAGMA query_only = ON;"
            : """
              PRAGMA journal_mode = WAL;
              PRAGMA synchronous = NORMAL;
              PRAGMA foreign_keys = ON;
              PRAGMA secure_delete = ON;
              """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
