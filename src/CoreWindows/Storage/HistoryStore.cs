using CoreWindows.Sensors;
using Microsoft.Data.Sqlite;

namespace CoreWindows.Storage;

/// <summary>Uma linha do histórico longo: o resumo de um sensor num minuto ou numa hora. Start em UTC.</summary>
public readonly record struct HistoryRow(DateTimeOffset Start, double Average, double Min, double Max, int Samples);

/// <summary>Números gerais do banco, para diagnóstico.</summary>
public sealed record HistoryStoreInfo(
    long Sensors, long MinuteRows, long HourRows,
    DateTimeOffset? FirstMinute, DateTimeOffset? LastMinute, long FileBytes);

/// <summary>
/// Histórico longo em SQLite: resumos por minuto (guardados por alguns dias) e por hora (guardados por um ano).
/// Cada operação abre a sua própria conexão (o Microsoft.Data.Sqlite reaproveita conexões por baixo),
/// então pode ser usado por várias threads: o gravador escreve, a interface lê.
/// Nada é aberto no construtor: o arquivo só é criado no primeiro uso.
/// </summary>
public sealed class HistoryStore
{
    public static readonly TimeSpan DefaultMinuteRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan DefaultHourRetention = TimeSpan.FromDays(365);

    private const int SchemaVersion = 1;

    private const string Schema = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS sensor (
            id        INTEGER PRIMARY KEY,
            key       TEXT NOT NULL UNIQUE,
            component TEXT NOT NULL,
            name      TEXT NOT NULL,
            kind      TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS minute (
            sensor    INTEGER NOT NULL,
            t         INTEGER NOT NULL,
            avg_value REAL    NOT NULL,
            min_value REAL    NOT NULL,
            max_value REAL    NOT NULL,
            samples   INTEGER NOT NULL,
            PRIMARY KEY (sensor, t)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS hour (
            sensor    INTEGER NOT NULL,
            t         INTEGER NOT NULL,
            avg_value REAL    NOT NULL,
            min_value REAL    NOT NULL,
            max_value REAL    NOT NULL,
            samples   INTEGER NOT NULL,
            PRIMARY KEY (sensor, t)
        ) WITHOUT ROWID;
        """;

    // Se a linha já existe (minuto gravado em duas partes, ou a hora que vai acumulando os minutos),
    // junta os dois resumos: média ponderada pelo nº de leituras, menor mínimo, maior máximo.
    private const string Upsert = """
        INSERT INTO {0} (sensor, t, avg_value, min_value, max_value, samples)
        VALUES ($sensor, $t, $avg, $min, $max, $samples)
        ON CONFLICT (sensor, t) DO UPDATE SET
            avg_value = (avg_value * samples + excluded.avg_value * excluded.samples) / (samples + excluded.samples),
            min_value = MIN(min_value, excluded.min_value),
            max_value = MAX(max_value, excluded.max_value),
            samples   = samples + excluded.samples;
        """;

    private readonly Lock _gate = new();
    private readonly string _connectionString;
    private readonly TimeSpan _minuteRetention;
    private readonly TimeSpan _hourRetention;
    private readonly Dictionary<string, long> _sensorIds = []; // id do sensor (texto) → id numérico no banco
    private bool _created;

    /// <param name="path">Caminho do arquivo .db. A pasta é criada se não existir.</param>
    public HistoryStore(string path, TimeSpan? minuteRetention = null, TimeSpan? hourRetention = null)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        _minuteRetention = minuteRetention ?? DefaultMinuteRetention;
        _hourRetention = hourRetention ?? DefaultHourRetention;
    }

    public string Path { get; }

    /// <summary>Caminho padrão: %LocalAppData%\&lt;app&gt;\history.db.</summary>
    public static string PathFor(string appName) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName, "history.db");

    /// <summary>
    /// Grava (ou atualiza) os dados fixos dos sensores. Precisa ser chamado antes de <see cref="Write"/>:
    /// resumos de sensores não registrados são ignorados.
    /// </summary>
    public void RegisterSensors(IEnumerable<SensorInfo> sensors)
    {
        using var connection = Open();
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sensor (key, component, name, kind) VALUES ($key, $component, $name, $kind)
                ON CONFLICT (key) DO UPDATE SET component = excluded.component, name = excluded.name, kind = excluded.kind;
                """;
            var key = Parameter(command, "$key");
            var component = Parameter(command, "$component");
            var name = Parameter(command, "$name");
            var kind = Parameter(command, "$kind");

            foreach (var sensor in sensors)
            {
                key.Value = sensor.Id;
                component.Value = sensor.ComponentId;
                name.Value = sensor.Name;
                kind.Value = sensor.Kind.ToString();
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        LoadSensorIds(connection);
    }

    /// <summary>Sensores que já apareceram no banco (inclusive de sessões anteriores).</summary>
    public IReadOnlyList<SensorInfo> ReadSensors()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, component, name, kind FROM sensor ORDER BY key;";

        var result = new List<SensorInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var kind = Enum.TryParse<SensorKind>(reader.GetString(3), out var parsed) ? parsed : SensorKind.Other;
            result.Add(new SensorInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2), kind));
        }
        return result;
    }

    /// <summary>
    /// Grava um minuto fechado: uma linha por sensor na tabela de minutos e a mesma leitura somada
    /// à hora correspondente, tudo numa transação só. Devolve quantos sensores foram gravados.
    /// </summary>
    public int Write(MinuteBatch batch)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var minute = UpsertCommand(connection, transaction, "minute");
        using var hour = UpsertCommand(connection, transaction, "hour");

        int written = 0;
        foreach (var aggregate in batch.Sensors)
        {
            long sensorId;
            lock (_gate)
            {
                if (!_sensorIds.TryGetValue(aggregate.SensorId, out sensorId))
                    continue;
            }

            Execute(minute, sensorId, batch.Minute, aggregate);
            Execute(hour, sensorId, batch.Minute / 60, aggregate);
            written++;
        }
        transaction.Commit();
        return written;
    }

    /// <summary>Resumos por minuto de um sensor, do mais antigo para o mais recente. Intervalo: [from, to).</summary>
    public IReadOnlyList<HistoryRow> ReadMinutes(string sensorId, DateTimeOffset from, DateTimeOffset to)
        => Read("minute", 60, sensorId, from, to);

    /// <summary>Resumos por hora de um sensor, do mais antigo para o mais recente. Intervalo: [from, to).</summary>
    public IReadOnlyList<HistoryRow> ReadHours(string sensorId, DateTimeOffset from, DateTimeOffset to)
        => Read("hour", 3600, sensorId, from, to);

    /// <summary>Apaga o que passou do prazo de retenção. Devolve quantas linhas saíram.</summary>
    public int Prune(DateTimeOffset now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var cutoff = Parameter(command, "$cutoff");

        command.CommandText = "DELETE FROM minute WHERE t < $cutoff;";
        cutoff.Value = (now - _minuteRetention).ToUnixTimeSeconds() / 60;
        int deleted = command.ExecuteNonQuery();

        command.CommandText = "DELETE FROM hour WHERE t < $cutoff;";
        cutoff.Value = (now - _hourRetention).ToUnixTimeSeconds() / 3600;
        deleted += command.ExecuteNonQuery();

        transaction.Commit();
        return deleted;
    }

    public HistoryStoreInfo GetInfo()
    {
        using var connection = Open();

        long? first = ScalarOrNull(connection, "SELECT MIN(t) FROM minute;");
        long? last = ScalarOrNull(connection, "SELECT MAX(t) FROM minute;");

        return new HistoryStoreInfo(
            Sensors: ScalarOrNull(connection, "SELECT COUNT(*) FROM sensor;") ?? 0,
            MinuteRows: ScalarOrNull(connection, "SELECT COUNT(*) FROM minute;") ?? 0,
            HourRows: ScalarOrNull(connection, "SELECT COUNT(*) FROM hour;") ?? 0,
            FirstMinute: first is { } f ? DateTimeOffset.FromUnixTimeSeconds(f * 60) : null,
            LastMinute: last is { } l ? DateTimeOffset.FromUnixTimeSeconds(l * 60) : null,
            FileBytes: File.Exists(Path) ? new FileInfo(Path).Length : 0);
    }

    private IReadOnlyList<HistoryRow> Read(string table, int secondsPerRow, string sensorId, DateTimeOffset from, DateTimeOffset to)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.t, m.avg_value, m.min_value, m.max_value, m.samples
            FROM {table} m JOIN sensor s ON s.id = m.sensor
            WHERE s.key = $key AND m.t >= $from AND m.t < $to
            ORDER BY m.t;
            """;
        Parameter(command, "$key").Value = sensorId;
        Parameter(command, "$from").Value = from.ToUnixTimeSeconds() / secondsPerRow;
        Parameter(command, "$to").Value = to.ToUnixTimeSeconds() / secondsPerRow;

        var rows = new List<HistoryRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new HistoryRow(
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0) * secondsPerRow),
                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), (int)reader.GetInt64(4)));
        }
        return rows;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            lock (_gate)
            {
                if (!_created)
                {
                    if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } directory)
                        Directory.CreateDirectory(directory);
                }

                connection.Open();

                if (!_created)
                {
                    CreateSchema(connection);
                    LoadSensorIds(connection);
                    _created = true;
                }
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA user_version;";
        long version = Convert.ToInt64(command.ExecuteScalar());
        if (version > SchemaVersion)
            throw new InvalidOperationException(
                $"O banco de histórico foi criado por uma versão mais nova do app (esquema {version}).");

        command.CommandText = Schema;
        command.ExecuteNonQuery();

        command.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        command.ExecuteNonQuery();
    }

    private void LoadSensorIds(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, id FROM sensor;";
        using var reader = command.ExecuteReader();

        lock (_gate)
        {
            while (reader.Read())
                _sensorIds[reader.GetString(0)] = reader.GetInt64(1);
        }
    }

    private static SqliteCommand UpsertCommand(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.Format(Upsert, table);
        foreach (string name in (string[])["$sensor", "$t", "$avg", "$min", "$max", "$samples"])
            Parameter(command, name);
        return command;
    }

    private static void Execute(SqliteCommand command, long sensorId, long time, SensorAggregate aggregate)
    {
        command.Parameters["$sensor"].Value = sensorId;
        command.Parameters["$t"].Value = time;
        command.Parameters["$avg"].Value = aggregate.Average;
        command.Parameters["$min"].Value = aggregate.Min;
        command.Parameters["$max"].Value = aggregate.Max;
        command.Parameters["$samples"].Value = aggregate.Samples;
        command.ExecuteNonQuery();
    }

    private static SqliteParameter Parameter(SqliteCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static long? ScalarOrNull(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }
}