using System.Collections.Generic;
using System.Threading.Tasks;
using HoangTLM.CodeBase.DatabaseScanner.Models;

namespace HoangTLM.CodeBase.DatabaseScanner.Interfaces
{
    public interface ISqliteRepository
    {
        Task InitializeDatabaseAsync(string schemaJsonPath);
        Task SyncProjectAsync(Project project);
        Task SyncSchemaAsync(string projectId, List<DbTable> tables, List<DbColumn> columns);
        Task SyncRoutinesAsync(string projectId, List<DbRoutine> routines, string outputSqlDirectory);
    }
}
