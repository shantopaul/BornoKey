using System.Text;
using BengaliKeyboard.Storage.Database;

namespace BengaliKeyboard.Prediction;

/// <summary>
/// Queries offline Unigram, Bigram, and Trigram language model tables in <c>dictionary.db</c> (ReadOnly mode).
/// </summary>
public sealed class NGramModel
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DictionaryService _dictionaryService;

    public NGramModel(
        SqliteConnectionFactory connectionFactory,
        DictionaryService dictionaryService)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(dictionaryService);

        _connectionFactory = connectionFactory;
        _dictionaryService = dictionaryService;
    }

    public async Task<IReadOnlyList<(string Word, int Frequency)>> QueryTrigramsAsync(
        string word1,
        string word2,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(word1) || string.IsNullOrWhiteSpace(word2))
        {
            return [];
        }

        await _dictionaryService.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var norm1 = word1.Trim().Normalize(NormalizationForm.FormC);
        var norm2 = word2.Trim().Normalize(NormalizationForm.FormC);
        var clampedLimit = Math.Clamp(limit, 1, 20);

        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateDictionaryConnectionAsync(readOnly: true, ct)
                    .ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Word3, Frequency
                    FROM Trigrams
                    WHERE Word1 = @w1 AND Word2 = @w2
                    ORDER BY Frequency DESC
                    LIMIT @limit;
                    """;
                command.Parameters.AddWithValue("@w1", norm1);
                command.Parameters.AddWithValue("@w2", norm2);
                command.Parameters.AddWithValue("@limit", clampedLimit);

                var results = new List<(string Word, int Frequency)>();
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    results.Add((reader.GetString(0), reader.GetInt32(1)));
                }

                return (IReadOnlyList<(string Word, int Frequency)>)results;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string Word, int Frequency)>> QueryBigramsAsync(
        string previousWord,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(previousWord))
        {
            return [];
        }

        await _dictionaryService.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var norm = previousWord.Trim().Normalize(NormalizationForm.FormC);
        var clampedLimit = Math.Clamp(limit, 1, 20);

        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateDictionaryConnectionAsync(readOnly: true, ct)
                    .ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Word2, Frequency
                    FROM Bigrams
                    WHERE Word1 = @w1
                    ORDER BY Frequency DESC
                    LIMIT @limit;
                    """;
                command.Parameters.AddWithValue("@w1", norm);
                command.Parameters.AddWithValue("@limit", clampedLimit);

                var results = new List<(string Word, int Frequency)>();
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    results.Add((reader.GetString(0), reader.GetInt32(1)));
                }

                return (IReadOnlyList<(string Word, int Frequency)>)results;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string Word, int Frequency)>> QueryTopUnigramsAsync(
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        await _dictionaryService.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        var clampedLimit = Math.Clamp(limit, 1, 20);

        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateDictionaryConnectionAsync(readOnly: true, ct)
                    .ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Word, Frequency
                    FROM BengaliWords
                    ORDER BY Frequency DESC
                    LIMIT @limit;
                    """;
                command.Parameters.AddWithValue("@limit", clampedLimit);

                var results = new List<(string Word, int Frequency)>();
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    results.Add((reader.GetString(0), reader.GetInt32(1)));
                }

                return (IReadOnlyList<(string Word, int Frequency)>)results;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
