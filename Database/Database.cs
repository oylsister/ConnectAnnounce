using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ConnectAnnounce.Database;


public class MessageDatabase(ConnectAnnounce core, ILogger<ConnectAnnounce> logger)
{
    private SqliteConnection? _connection;
    private readonly ConnectAnnounce _core = core;
    private readonly ILogger<ConnectAnnounce> _logger = logger;

    public async Task DatabaseOnLoad()
    {
        _connection = new SqliteConnection($"Data Source={Path.Join(_core.ModuleDirectory, "zsharpdatabase.db")}");
        _connection.Open();

        _logger.LogInformation("[DatabaseOnLoad] Database has been created. to {0}", Path.Join(_core.ModuleDirectory, "zsharpdatabase.db"));

        await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS player_message (player_auth TEXT PRIMARY KEY, player_msg VARCHAR(256));");
    }

    public void DatabaseOnUnload()
    {
        _connection?.Close();
    }

    public async Task<string?> GetPlayerMessage(ulong steamid)
    {
        if(_connection == null)
        {
            _logger.LogError("[GetPlayerMessage] SqlConnection is null");
            return null;
        }

        var query = @"SELECT player_msg FROM player_message WHERE player_auth = @Auth;";
        var reader = await _connection.ExecuteReaderAsync(query, new {
            Auth = steamid.ToString()
        });

        if(await reader.ReadAsync())
        {
            var message = reader["player_msg"].ToString();
            return message;
        }

        return null;
    }

    public async Task InsertPlayerMessage(ulong steamid, string message)
    {
        if(_connection == null)
        {
            _logger.LogError("[InsertPlayerMessage] SqlConnection is null!");
            return;
        }

        if(string.IsNullOrEmpty(message))
        {
            _logger.LogError("[InsertPlayerMessage] {auth} insert a null or empty message!", steamid);
            return;
        }

        var query = @"INSERT INTO player_message (player_auth, player_msg) VALUES(@Auth, @Msg) ON CONFLICT(player_auth) DO UPDATE SET player_msg = @Msg;";
        
        await _connection.ExecuteAsync(query, new {
            Auth = steamid.ToString(),
            Msg = message
        });
    }
}