using System.Collections.Generic;
using System.Threading.Tasks;
using HoangTLM.CodeBase.DatabaseScanner.Models;

namespace HoangTLM.CodeBase.DatabaseScanner.Interfaces
{
    public interface ISqlServerScanner
    {
        Task<(List<DbTable> Tables, List<DbColumn> Columns)> ScanSchemaAsync(string connectionString, string projectId, string databaseName);
        Task<List<DbRoutine>> ScanRoutinesAsync(string connectionString);
    }
}
