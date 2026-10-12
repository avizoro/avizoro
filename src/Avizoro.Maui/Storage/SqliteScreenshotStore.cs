using Avizoro.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Avizoro.Maui.Storage;

/// <summary>
/// SQLite-backed store for screenshot blobs, keyed by event id. Shares the same
/// database file as the event queue but uses its own table, so image bytes stay
/// out of the JSON event payloads.
/// </summary>
public class SqliteScreenshotStore : IScreenshotStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public SqliteScreenshotStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task SaveAsync(Guid eventId, byte[] jpeg)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO Screenshots (EventId, Jpeg, CreatedTicks) VALUES ($id, $jpeg, $ticks)";
        command.Parameters.AddWithValue("$id", eventId.ToString());
        command.Parameters.AddWithValue("$jpeg", jpeg);
        command.Parameters.AddWithValue("$ticks", DateTime.UtcNow.Ticks);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<byte[]?> GetAsync(Guid eventId)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Jpeg FROM Screenshots WHERE EventId = $id";
        command.Parameters.AddWithValue("$id", eventId.ToString());

        var result = await command.ExecuteScalarAsync();
        return result as byte[];
    }

    public async Task DeleteAsync(Guid eventId)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Screenshots WHERE EventId = $id";
        command.Parameters.AddWithValue("$id", eventId.ToString());

        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS Screenshots (EventId TEXT PRIMARY KEY, Jpeg BLOB NOT NULL, CreatedTicks INTEGER NOT NULL)";
            await command.ExecuteNonQueryAsync();

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }
}
