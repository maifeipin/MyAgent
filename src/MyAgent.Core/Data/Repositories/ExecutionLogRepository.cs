using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using MyAgent.Core.Data.Models;

namespace MyAgent.Core.Data.Repositories;

public class ExecutionLogRepository : IExecutionLogRepository
{
    private readonly string _connectionString;

    public ExecutionLogRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InsertExecutionLogAsync(ExecutionLog log)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(@"
            INSERT INTO ExecutionLogs (Id, SkillId, StartTime, EndTime, Status, TriggerMode)
            VALUES (@Id, @SkillId, @StartTime, @EndTime, @Status, @TriggerMode)", log);
    }

    public async Task UpdateExecutionLogStatusAsync(string id, int status, DateTime endTime)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(@"
            UPDATE ExecutionLogs 
            SET Status = @status, EndTime = @endTime 
            WHERE Id = @id", new { id, status, endTime });
    }

    public async Task InsertStepLogAsync(StepLog step)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(@"
            INSERT INTO StepLogs (Id, ExecutionId, StepId, ActionName, DurationMs, Status, RawInput, RawOutput)
            VALUES (@Id, @ExecutionId, @StepId, @ActionName, @DurationMs, @Status, @RawInput, @RawOutput)", step);
    }

    public async Task<IEnumerable<ExecutionLog>> GetRecentLogsAsync(string skillId, int limit = 10)
    {
        using var connection = new SqliteConnection(_connectionString);
        return await connection.QueryAsync<ExecutionLog>(@"
            SELECT * FROM ExecutionLogs 
            WHERE SkillId = @skillId 
            ORDER BY StartTime DESC 
            LIMIT @limit", new { skillId, limit });
    }

    public async Task<IEnumerable<StepLog>> GetStepLogsAsync(string executionId)
    {
        using var connection = new SqliteConnection(_connectionString);
        return await connection.QueryAsync<StepLog>(@"
            SELECT * FROM StepLogs 
            WHERE ExecutionId = @executionId 
            ORDER BY Id ASC", new { executionId });
    }
}
