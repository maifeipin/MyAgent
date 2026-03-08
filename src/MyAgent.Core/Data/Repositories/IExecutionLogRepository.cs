using System.Collections.Generic;
using System.Threading.Tasks;
using MyAgent.Core.Data.Models;

namespace MyAgent.Core.Data.Repositories;

public interface IExecutionLogRepository
{
    Task InsertExecutionLogAsync(ExecutionLog log);
    Task UpdateExecutionLogStatusAsync(string id, int status, System.DateTime endTime);
    Task InsertStepLogAsync(StepLog step);
    Task<IEnumerable<ExecutionLog>> GetRecentLogsAsync(string skillId, int limit = 10);
    Task<IEnumerable<StepLog>> GetStepLogsAsync(string executionId);
}
