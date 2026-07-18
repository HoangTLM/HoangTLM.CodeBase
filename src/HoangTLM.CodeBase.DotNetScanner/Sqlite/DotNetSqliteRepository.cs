using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using HoangTLM.CodeBase.DotNetScanner.Models;

namespace HoangTLM.CodeBase.DotNetScanner.Sqlite
{
    public class DotNetSqliteRepository
    {
        private readonly string _connectionString;

        public DotNetSqliteRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task SyncSolutionEntitiesAsync(string projectId, List<CodeEntity> scannedEntities)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                var scannedEntityIds = new HashSet<string>();
                var scannedFileIds = new HashSet<string>();

                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var entity in scannedEntities)
                    {
                        scannedEntityIds.Add(entity.Id);
                        scannedFileIds.Add(entity.FileId);

                        // 1. Ensure File exists in the files table
                        string checkFileSql = "SELECT id FROM files WHERE id = @id;";
                        bool fileExists = false;
                        using (var command = new SqliteCommand(checkFileSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@id", entity.FileId);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                fileExists = await reader.ReadAsync();
                            }
                        }

                        if (!fileExists)
                        {
                            string insertFileSql = "INSERT INTO files (id, project_id, relative_path, absolute_path) VALUES (@id, @projectId, @relativePath, @absolutePath);";
                            using (var command = new SqliteCommand(insertFileSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", entity.FileId);
                                command.Parameters.AddWithValue("@projectId", projectId);
                                command.Parameters.AddWithValue("@relativePath", entity.RelativePath);
                                command.Parameters.AddWithValue("@absolutePath", entity.AbsolutePath);
                                await command.ExecuteNonQueryAsync();
                            }
                        }

                        // 2. Incremental Sync (Upsert) Entity
                        string checkEntitySql = "SELECT id, signature, metadata, start_line, end_line FROM entities WHERE id = @id;";
                        bool entityExists = false;
                        string dbSignature = null;
                        string dbMetadata = null;
                        int dbStartLine = 0;
                        int dbEndLine = 0;

                        using (var command = new SqliteCommand(checkEntitySql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@id", entity.Id);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    entityExists = true;
                                    dbSignature = reader["signature"] != DBNull.Value ? reader["signature"].ToString() : null;
                                    dbMetadata = reader["metadata"] != DBNull.Value ? reader["metadata"].ToString() : null;
                                    dbStartLine = Convert.ToInt32(reader["start_line"]);
                                    dbEndLine = Convert.ToInt32(reader["end_line"]);
                                }
                            }
                        }

                        if (!entityExists)
                        {
                            // Fresh Insert
                            string insertSql = @"
                                INSERT INTO entities (id, file_id, name, type, signature, start_line, end_line, metadata, description)
                                VALUES (@id, @fileId, @name, @type, @signature, @startLine, @endLine, @metadata, @description);";

                            using (var command = new SqliteCommand(insertSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", entity.Id);
                                command.Parameters.AddWithValue("@fileId", entity.FileId);
                                command.Parameters.AddWithValue("@name", entity.Name);
                                command.Parameters.AddWithValue("@type", entity.Type);
                                command.Parameters.AddWithValue("@signature", entity.Signature);
                                command.Parameters.AddWithValue("@startLine", entity.StartLine);
                                command.Parameters.AddWithValue("@endLine", entity.EndLine);
                                command.Parameters.AddWithValue("@metadata", entity.Metadata);
                                command.Parameters.AddWithValue("@description", DBNull.Value); // Keep description empty for user input
                                await command.ExecuteNonQueryAsync();
                            }
                            Console.WriteLine($"[DotNetScanner] Inserted {entity.Type}: {entity.Name}");
                        }
                        else
                        {
                            // Check if metadata, signature, or line positions changed, and update if necessary (preserving description!)
                            if (dbSignature != entity.Signature || dbMetadata != entity.Metadata || dbStartLine != entity.StartLine || dbEndLine != entity.EndLine)
                            {
                                string updateSql = @"
                                    UPDATE entities
                                    SET signature = @signature, metadata = @metadata, start_line = @startLine, end_line = @endLine
                                    WHERE id = @id;";

                                using (var command = new SqliteCommand(updateSql, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@id", entity.Id);
                                    command.Parameters.AddWithValue("@signature", entity.Signature);
                                    command.Parameters.AddWithValue("@metadata", entity.Metadata);
                                    command.Parameters.AddWithValue("@startLine", entity.StartLine);
                                    command.Parameters.AddWithValue("@endLine", entity.EndLine);
                                    await command.ExecuteNonQueryAsync();
                                }
                                Console.WriteLine($"[DotNetScanner] Updated {entity.Type}: {entity.Name}");
                            }
                        }
                    }

                    // 3. Delete Obsolete Entities
                    // Fetch existing entities mapped to .NET Source files under this project
                    var existingCsharpEntities = new List<(string Id, string FileId)>();
                    string fetchSql = @"
                        SELECT e.id, e.file_id 
                        FROM entities e
                        INNER JOIN files f ON e.file_id = f.id
                        WHERE f.project_id = @projectId AND e.type IN ('class', 'interface', 'method', 'enum', 'const', 'endpoint', 'queue', 'schedule', 'controller');";

                    using (var command = new SqliteCommand(fetchSql, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@projectId", projectId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                existingCsharpEntities.Add((reader["id"].ToString(), reader["file_id"].ToString()));
                            }
                        }
                    }

                    foreach (var entry in existingCsharpEntities)
                    {
                        if (!scannedEntityIds.Contains(entry.Id))
                        {
                            string deleteEntitySql = "DELETE FROM entities WHERE id = @id;";
                            using (var command = new SqliteCommand(deleteEntitySql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", entry.Id);
                                await command.ExecuteNonQueryAsync();
                            }
                            Console.WriteLine($"[DotNetScanner] Deleted obsolete entity ID: {entry.Id}");
                        }
                    }

                    // 4. Delete files that no longer contain any entities
                    string fetchFilesSql = "SELECT id FROM files WHERE project_id = @projectId;";
                    var existingFiles = new List<string>();
                    using (var command = new SqliteCommand(fetchFilesSql, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@projectId", projectId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                existingFiles.Add(reader["id"].ToString());
                            }
                        }
                    }

                    foreach (var fileId in existingFiles)
                    {
                        string checkUsageSql = "SELECT COUNT(*) FROM entities WHERE file_id = @fileId;";
                        long usageCount = 0;
                        using (var command = new SqliteCommand(checkUsageSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@fileId", fileId);
                            usageCount = (long)await command.ExecuteScalarAsync();
                        }

                        if (usageCount == 0)
                        {
                            string deleteFileSql = "DELETE FROM files WHERE id = @id;";
                            using (var command = new SqliteCommand(deleteFileSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", fileId);
                                await command.ExecuteNonQueryAsync();
                            }
                            Console.WriteLine($"[DotNetScanner] Deleted obsolete file association: {fileId}");
                        }
                    }

                    await transaction.CommitAsync();
                }
            }
        }
    }
}
