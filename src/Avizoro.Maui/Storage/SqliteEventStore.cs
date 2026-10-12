using System.Text.Json;
using Avizoro.Core.Models;
using Avizoro.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Avizoro.Maui.Storage;

/// <summary>
/// SQLite-backed offline queue built on Microsoft.Data.Sqlite (the maintained,
/// official ADO.NET provider). Events are stored as JSON payloads keyed by id
/// and ordered by creation time, so the uploader can drain them oldest-first.
/// </summary>
public class SqliteEventStore : IEventStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public SqliteEventStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task SaveAsync(AvizoroEvent avizoroEvent)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO Events (Id, Payload, CreatedTicks) VALUES ($id, $payload, $ticks)";
        command.Parameters.AddWithValue("$id", avizoroEvent.Id.ToString());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(avizoroEvent));
        command.Parameters.AddWithValue("$ticks", avizoroEvent.Timestamp.Ticks);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AvizoroEvent>> GetPendingAsync(int max = 50)
    {
        await EnsureInitializedAsync();

        var events = new List<AvizoroEvent>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM Events ORDER BY CreatedTicks LIMIT $max";
        command.Parameters.AddWithValue("$max", max);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var avizoroEvent = JsonSerializer.Deserialize<AvizoroEvent>(reader.GetString(0));
            if (avizoroEvent is not null)
                events.Add(avizoroEvent);
        }

        return events;
    }

    public async Task DeleteAsync(Guid id)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Events WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountAsync()
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Events";

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
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
                "CREATE TABLE IF NOT EXISTS Events (Id TEXT PRIMARY KEY, Payload TEXT NOT NULL, CreatedTicks INTEGER NOT NULL)";
            await command.ExecuteNonQueryAsync();

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }
}
