using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyAgent.Core.Data;

public class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(string connectionString, ILogger<DatabaseInitializer> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public void Initialize()
    {
        // 自动提取数据库文件路径并创建所在目录
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        var dbPath = builder.DataSource;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            _logger.LogInformation("Created database directory at {Dir}", dir);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // ExecutionLogs 表
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS ExecutionLogs (
                Id TEXT PRIMARY KEY,
                SkillId TEXT NOT NULL,
                StartTime DATETIME NOT NULL,
                EndTime DATETIME,
                Status INTEGER NOT NULL,
                TriggerMode INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ExecutionLogs_SkillId ON ExecutionLogs(SkillId);
        ");

        // StepLogs 表
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS StepLogs (
                Id TEXT PRIMARY KEY,
                ExecutionId TEXT NOT NULL,
                StepId TEXT NOT NULL,
                ActionName TEXT NOT NULL,
                DurationMs INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                RawInput TEXT,
                RawOutput TEXT,
                FOREIGN KEY(ExecutionId) REFERENCES ExecutionLogs(Id)
            );
        ");

        // Analytics 表
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS Analytics (
                Date DATE NOT NULL,
                SkillId TEXT NOT NULL,
                TotalExecutions INTEGER NOT NULL,
                SuccessRate REAL NOT NULL,
                TokensUsed INTEGER NOT NULL,
                PRIMARY KEY(Date, SkillId)
            );
        ");

        _logger.LogInformation("Database initialized and tables verified.");
    }
}
