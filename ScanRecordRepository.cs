using Microsoft.Data.Sqlite;

namespace HostComputerApp;

public sealed class ScanRecordRepository
{
    private readonly string _databasePath;
    private readonly AppLogger _logger;

    public ScanRecordRepository(AppLogger logger)
    {
        _logger = logger;
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        _databasePath = Path.Combine(dataDirectory, "scans.db");
        Initialize();
    }

    public void Insert(ScanRecord record)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO scan_records (scan_time, batch_id, position_no, product_code, status)
            VALUES ($scan_time, $batch_id, $position_no, $product_code, $status);
            """;
        command.Parameters.AddWithValue("$scan_time", record.ScanTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        command.Parameters.AddWithValue("$batch_id", record.BatchId);
        command.Parameters.AddWithValue("$position_no", record.PositionNo);
        command.Parameters.AddWithValue("$product_code", record.ProductCode);
        command.Parameters.AddWithValue("$status", record.Status);
        command.ExecuteNonQuery();
    }

    public List<ScanRecordView> QueryLatest(int take)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scan_time, batch_id, position_no, product_code, status
            FROM scan_records
            ORDER BY id DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", take);

        using var reader = command.ExecuteReader();
        var records = new List<ScanRecordView>();
        while (reader.Read())
        {
            records.Add(new ScanRecordView
            {
                ID = reader.GetInt64(0),
                扫码时间 = reader.GetString(1),
                批次号 = reader.GetString(2),
                位置 = reader.GetInt32(3),
                产品码 = reader.GetString(4),
                状态 = reader.GetString(5),
            });
        }

        return records;
    }

    private void Initialize()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS scan_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    scan_time TEXT NOT NULL,
                    batch_id TEXT NOT NULL,
                    position_no INTEGER NOT NULL,
                    product_code TEXT NOT NULL,
                    status TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_scan_records_batch ON scan_records(batch_id);
                CREATE INDEX IF NOT EXISTS idx_scan_records_time ON scan_records(scan_time);
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Error($"初始化SQLite数据库失败：{ex.Message}");
            throw;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }
}

public sealed record ScanRecord(long Id, DateTime ScanTime, string BatchId, int PositionNo, string ProductCode, string Status);

public sealed class ScanRecordView
{
    public long ID { get; set; }
    public string 扫码时间 { get; set; } = "";
    public string 批次号 { get; set; } = "";
    public int 位置 { get; set; }
    public string 产品码 { get; set; } = "";
    public string 状态 { get; set; } = "";
}
