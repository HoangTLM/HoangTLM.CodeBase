using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using HoangTLM.CodeBase.DatabaseScanner.Interfaces;
using HoangTLM.CodeBase.DatabaseScanner.Models;

namespace HoangTLM.CodeBase.DatabaseScanner.Sqlite
{
    public class SqliteRepository : ISqliteRepository
    {
        private readonly string _connectionString;

        public SqliteRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private string ComputeHash(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder();
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public async Task InitializeDatabaseAsync(string schemaJsonPath)
        {
            if (!File.Exists(schemaJsonPath))
            {
                throw new FileNotFoundException($"Schema definition file not found at: {schemaJsonPath}");
            }

            var jsonContent = await File.ReadAllTextAsync(schemaJsonPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var schemaData = JsonSerializer.Deserialize<SchemaJson>(jsonContent, options);

            if (schemaData?.database_schema?.tables == null)
            {
                throw new InvalidOperationException("Invalid schema.json structure.");
            }

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var table in schemaData.database_schema.tables)
                    {
                        string createSql;
                        if (table.type == "virtual_table")
                        {
                            var ftsCols = new List<string>();
                            foreach (var col in table.columns)
                            {
                                var colDef = col.name;
                                if (col.note == "UNINDEXED")
                                {
                                    colDef += " UNINDEXED";
                                }
                                ftsCols.Add(colDef);
                            }
                            createSql = $"CREATE VIRTUAL TABLE IF NOT EXISTS [{table.table_name}] USING fts5(\n  {string.Join(",\n  ", ftsCols)}\n);";
                        }
                        else
                        {
                            var colDefs = new List<string>();
                            var fkDefs = new List<string>();
                            foreach (var col in table.columns)
                            {
                                var def = $"[{col.name}] {col.type}";
                                if (col.is_primary_key == true)
                                {
                                    def += " PRIMARY KEY";
                                }
                                colDefs.Add(def);

                                if (col.is_foreign_key == true && !string.IsNullOrEmpty(col.references))
                                {
                                    var parts = col.references.Split('.');
                                    if (parts.Length == 2)
                                    {
                                        fkDefs.Add($"FOREIGN KEY([{col.name}]) REFERENCES [{parts[0]}]([{parts[1]}])");
                                    }
                                }
                            }
                            createSql = $"CREATE TABLE IF NOT EXISTS [{table.table_name}] (\n  {string.Join(",\n  ", colDefs)}";
                            if (fkDefs.Count > 0)
                            {
                                createSql += ",\n  " + string.Join(",\n  ", fkDefs);
                            }
                            createSql += "\n);";
                        }

                        using (var command = new SqliteCommand(createSql, connection, transaction))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    await transaction.CommitAsync();
                }
            }
        }

        public async Task SyncProjectAsync(Project project)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                string selectQuery = "SELECT id, parent_id, name, path, type, language, description FROM projects WHERE id = @id;";
                Project existing = null;

                using (var command = new SqliteCommand(selectQuery, connection))
                {
                    command.Parameters.AddWithValue("@id", project.Id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            existing = new Project
                            {
                                Id = reader["id"].ToString(),
                                ParentId = reader["parent_id"] != DBNull.Value ? reader["parent_id"].ToString() : null,
                                Name = reader["name"].ToString(),
                                Path = reader["path"] != DBNull.Value ? reader["path"].ToString() : null,
                                Type = reader["type"] != DBNull.Value ? reader["type"].ToString() : null,
                                Language = reader["language"] != DBNull.Value ? reader["language"].ToString() : null,
                                Description = reader["description"] != DBNull.Value ? reader["description"].ToString() : null
                            };
                        }
                    }
                }

                if (existing == null)
                {
                    string insertQuery = @"
                        INSERT INTO projects (id, parent_id, name, path, type, language, scanned_at, description)
                        VALUES (@id, @parentId, @name, @path, @type, @language, @scannedAt, @description);";

                    using (var command = new SqliteCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@id", project.Id);
                        command.Parameters.AddWithValue("@parentId", (object)project.ParentId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@name", project.Name);
                        command.Parameters.AddWithValue("@path", (object)project.Path ?? DBNull.Value);
                        command.Parameters.AddWithValue("@type", (object)project.Type ?? DBNull.Value);
                        command.Parameters.AddWithValue("@language", (object)project.Language ?? DBNull.Value);
                        command.Parameters.AddWithValue("@scannedAt", project.ScannedAt.ToString("o"));
                        command.Parameters.AddWithValue("@description", (object)project.Description ?? DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine($"[Project] Inserted project: {project.Name}");
                }
                else
                {
                    bool needsUpdate = existing.ParentId != project.ParentId ||
                                       existing.Name != project.Name ||
                                       existing.Path != project.Path ||
                                       existing.Type != project.Type ||
                                       existing.Language != project.Language ||
                                       existing.Description != project.Description;

                    if (needsUpdate)
                    {
                        string updateQuery = @"
                            UPDATE projects 
                            SET parent_id = @parentId, name = @name, path = @path, type = @type, 
                                language = @language, scanned_at = @scannedAt, description = @description
                            WHERE id = @id;";

                        using (var command = new SqliteCommand(updateQuery, connection))
                        {
                            command.Parameters.AddWithValue("@id", project.Id);
                            command.Parameters.AddWithValue("@parentId", (object)project.ParentId ?? DBNull.Value);
                            command.Parameters.AddWithValue("@name", project.Name);
                            command.Parameters.AddWithValue("@path", (object)project.Path ?? DBNull.Value);
                            command.Parameters.AddWithValue("@type", (object)project.Type ?? DBNull.Value);
                            command.Parameters.AddWithValue("@language", (object)project.Language ?? DBNull.Value);
                            command.Parameters.AddWithValue("@scannedAt", project.ScannedAt.ToString("o"));
                            command.Parameters.AddWithValue("@description", (object)project.Description ?? DBNull.Value);

                            await command.ExecuteNonQueryAsync();
                        }
                        Console.WriteLine($"[Project] Updated project: {project.Name}");
                    }
                    else
                    {
                        Console.WriteLine($"[Project] Project '{project.Name}' has no changes.");
                    }
                }
            }
        }

        public async Task SyncSchemaAsync(string projectId, List<DbTable> tables, List<DbColumn> columns)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                var scannedTablesMap = new Dictionary<string, DbTable>();
                var scannedTableNames = new HashSet<string>();

                foreach (var t in tables)
                {
                    t.Id = ComputeHash($"{projectId}:{t.SchemaName}.{t.Name}");
                    scannedTablesMap[t.Id] = t;
                    scannedTableNames.Add(t.Id);
                }

                var existingTables = new Dictionary<string, DbTable>();
                string getTablesQuery = "SELECT id, project_id, name, schema_name, database_name, description, metadata FROM db_tables WHERE project_id = @projectId;";
                using (var command = new SqliteCommand(getTablesQuery, connection))
                {
                    command.Parameters.AddWithValue("@projectId", projectId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var t = new DbTable
                            {
                                Id = reader["id"].ToString(),
                                ProjectId = reader["project_id"].ToString(),
                                Name = reader["name"].ToString(),
                                SchemaName = reader["schema_name"].ToString(),
                                DatabaseName = reader["database_name"].ToString(),
                                Description = reader["description"] != DBNull.Value ? reader["description"].ToString() : null,
                                Metadata = reader["metadata"] != DBNull.Value ? reader["metadata"].ToString() : null
                            };
                            existingTables[t.Id] = t;
                        }
                    }
                }

                using (var transaction = connection.BeginTransaction())
                {
                    // 1. Sync Tables
                    foreach (var scannedTable in tables)
                    {
                        if (!existingTables.ContainsKey(scannedTable.Id))
                        {
                            string insertSql = @"
                                INSERT INTO db_tables (id, project_id, name, schema_name, database_name, description, metadata)
                                VALUES (@id, @projectId, @name, @schemaName, @databaseName, @description, @metadata);";
                            using (var command = new SqliteCommand(insertSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", scannedTable.Id);
                                command.Parameters.AddWithValue("@projectId", projectId);
                                command.Parameters.AddWithValue("@name", scannedTable.Name);
                                command.Parameters.AddWithValue("@schemaName", scannedTable.SchemaName);
                                command.Parameters.AddWithValue("@databaseName", scannedTable.DatabaseName);
                                command.Parameters.AddWithValue("@description", (object)scannedTable.Description ?? DBNull.Value);
                                command.Parameters.AddWithValue("@metadata", (object)scannedTable.Metadata ?? DBNull.Value);
                                await command.ExecuteNonQueryAsync();
                            }
                            Console.WriteLine($"[Table] Inserted table: {scannedTable.SchemaName}.{scannedTable.Name}");
                        }
                        else
                        {
                            var existingTable = existingTables[scannedTable.Id];
                            bool tableChanged = existingTable.Name != scannedTable.Name ||
                                                existingTable.SchemaName != scannedTable.SchemaName ||
                                                existingTable.DatabaseName != scannedTable.DatabaseName ||
                                                existingTable.Description != scannedTable.Description;

                            if (tableChanged)
                            {
                                string updateSql = @"
                                    UPDATE db_tables 
                                    SET name = @name, schema_name = @schemaName, database_name = @databaseName, description = @description
                                    WHERE id = @id;";
                                using (var command = new SqliteCommand(updateSql, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@id", scannedTable.Id);
                                    command.Parameters.AddWithValue("@name", scannedTable.Name);
                                    command.Parameters.AddWithValue("@schemaName", scannedTable.SchemaName);
                                    command.Parameters.AddWithValue("@databaseName", scannedTable.DatabaseName);
                                    command.Parameters.AddWithValue("@description", (object)scannedTable.Description ?? DBNull.Value);
                                    await command.ExecuteNonQueryAsync();
                                }
                                Console.WriteLine($"[Table] Updated table: {scannedTable.SchemaName}.{scannedTable.Name}");
                            }
                        }
                    }

                    // 2. Sync Columns
                    foreach (var scannedTable in tables)
                    {
                        var tableId = scannedTable.Id;
                        var tableScannedColumns = columns.FindAll(c => c.SchemaName == scannedTable.SchemaName && c.TableName == scannedTable.Name);

                        var existingColumns = new Dictionary<string, DbColumn>();
                        string getColsQuery = @"
                            SELECT id, table_id, name, data_type, max_length, is_nullable, is_primary_key, is_foreign_key, fk_table, fk_column, default_val, description 
                            FROM db_columns WHERE table_id = @tableId;";
                        using (var command = new SqliteCommand(getColsQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@tableId", tableId);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    var c = new DbColumn
                                    {
                                        Id = reader["id"].ToString(),
                                        TableId = reader["table_id"].ToString(),
                                        Name = reader["name"].ToString(),
                                        DataType = reader["data_type"].ToString(),
                                        MaxLength = reader["max_length"] != DBNull.Value ? Convert.ToInt32(reader["max_length"]) : (int?)null,
                                        IsNullable = Convert.ToInt32(reader["is_nullable"]),
                                        IsPrimaryKey = Convert.ToInt32(reader["is_primary_key"]),
                                        IsForeignKey = Convert.ToInt32(reader["is_foreign_key"]),
                                        FkTable = reader["fk_table"] != DBNull.Value ? reader["fk_table"].ToString() : null,
                                        FkColumn = reader["fk_column"] != DBNull.Value ? reader["fk_column"].ToString() : null,
                                        DefaultVal = reader["default_val"] != DBNull.Value ? reader["default_val"].ToString() : null,
                                        Description = reader["description"] != DBNull.Value ? reader["description"].ToString() : null
                                    };
                                    existingColumns[c.Id] = c;
                                }
                            }
                        }

                        var scannedColumnIds = new HashSet<string>();

                        foreach (var sc in tableScannedColumns)
                        {
                            sc.TableId = tableId;
                            sc.Id = ComputeHash($"{tableId}:{sc.Name}");
                            scannedColumnIds.Add(sc.Id);

                            if (!existingColumns.ContainsKey(sc.Id))
                            {
                                string insertColSql = @"
                                    INSERT INTO db_columns (id, table_id, name, data_type, max_length, is_nullable, is_primary_key, is_foreign_key, fk_table, fk_column, default_val, description)
                                    VALUES (@id, @tableId, @name, @dataType, @maxLength, @isNullable, @isPrimaryKey, @isForeignKey, @fkTable, @fkColumn, @defaultVal, @description);";
                                using (var command = new SqliteCommand(insertColSql, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@id", sc.Id);
                                    command.Parameters.AddWithValue("@tableId", tableId);
                                    command.Parameters.AddWithValue("@name", sc.Name);
                                    command.Parameters.AddWithValue("@dataType", sc.DataType);
                                    command.Parameters.AddWithValue("@maxLength", (object)sc.MaxLength ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@isNullable", sc.IsNullable);
                                    command.Parameters.AddWithValue("@isPrimaryKey", sc.IsPrimaryKey);
                                    command.Parameters.AddWithValue("@isForeignKey", sc.IsForeignKey);
                                    command.Parameters.AddWithValue("@fkTable", (object)sc.FkTable ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@fkColumn", (object)sc.FkColumn ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@defaultVal", (object)sc.DefaultVal ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@description", (object)sc.Description ?? DBNull.Value);
                                    await command.ExecuteNonQueryAsync();
                                }
                                Console.WriteLine($"  [Column] Inserted: {scannedTable.SchemaName}.{scannedTable.Name}.{sc.Name}");
                            }
                            else
                            {
                                var ec = existingColumns[sc.Id];
                                bool changed = ec.DataType != sc.DataType ||
                                               ec.MaxLength != sc.MaxLength ||
                                               ec.IsNullable != sc.IsNullable ||
                                               ec.IsPrimaryKey != sc.IsPrimaryKey ||
                                               ec.IsForeignKey != sc.IsForeignKey ||
                                               ec.FkTable != sc.FkTable ||
                                               ec.FkColumn != sc.FkColumn ||
                                               ec.DefaultVal != sc.DefaultVal ||
                                               ec.Description != sc.Description;

                                if (changed)
                                {
                                    string updateColSql = @"
                                        UPDATE db_columns 
                                        SET data_type = @dataType, max_length = @maxLength, is_nullable = @isNullable, 
                                            is_primary_key = @isPrimaryKey, is_foreign_key = @isForeignKey, 
                                            fk_table = @fkTable, fk_column = @fkColumn, default_val = @defaultVal, description = @description
                                        WHERE id = @id;";
                                    using (var command = new SqliteCommand(updateColSql, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@id", sc.Id);
                                        command.Parameters.AddWithValue("@dataType", sc.DataType);
                                        command.Parameters.AddWithValue("@maxLength", (object)sc.MaxLength ?? DBNull.Value);
                                        command.Parameters.AddWithValue("@isNullable", sc.IsNullable);
                                        command.Parameters.AddWithValue("@isPrimaryKey", sc.IsPrimaryKey);
                                        command.Parameters.AddWithValue("@isForeignKey", sc.IsForeignKey);
                                        command.Parameters.AddWithValue("@fkTable", (object)sc.FkTable ?? DBNull.Value);
                                        command.Parameters.AddWithValue("@fkColumn", (object)sc.FkColumn ?? DBNull.Value);
                                        command.Parameters.AddWithValue("@defaultVal", (object)sc.DefaultVal ?? DBNull.Value);
                                        command.Parameters.AddWithValue("@description", (object)sc.Description ?? DBNull.Value);
                                        await command.ExecuteNonQueryAsync();
                                    }
                                    Console.WriteLine($"  [Column] Updated: {scannedTable.SchemaName}.{scannedTable.Name}.{sc.Name}");
                                }
                            }
                        }

                        foreach (var ecId in existingColumns.Keys)
                        {
                            if (!scannedColumnIds.Contains(ecId))
                            {
                                string deleteColSql = "DELETE FROM db_columns WHERE id = @id;";
                                using (var command = new SqliteCommand(deleteColSql, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@id", ecId);
                                    await command.ExecuteNonQueryAsync();
                                }
                                Console.WriteLine($"  [Column] Deleted: {scannedTable.SchemaName}.{scannedTable.Name}.{existingColumns[ecId].Name}");
                            }
                        }
                    }

                    // 3. Clean up deleted tables
                    foreach (var etId in existingTables.Keys)
                    {
                        if (!scannedTableNames.Contains(etId))
                        {
                            string deleteColsSql = "DELETE FROM db_columns WHERE table_id = @tableId;";
                            using (var command = new SqliteCommand(deleteColsSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@tableId", etId);
                                await command.ExecuteNonQueryAsync();
                            }

                            string deleteTableSql = "DELETE FROM db_tables WHERE id = @id;";
                            using (var command = new SqliteCommand(deleteTableSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", etId);
                                await command.ExecuteNonQueryAsync();
                            }
                            Console.WriteLine($"[Table] Deleted: {existingTables[etId].SchemaName}.{existingTables[etId].Name}");
                        }
                    }

                    await transaction.CommitAsync();
                }
            }
        }

        public async Task SyncRoutinesAsync(string projectId, List<DbRoutine> routines, string outputSqlDirectory)
        {
            if (!Directory.Exists(outputSqlDirectory))
            {
                Directory.CreateDirectory(outputSqlDirectory);
            }

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                var scannedRoutineIds = new HashSet<string>();
                var routineMetadataList = new List<(string FileId, string EntityId, DbRoutine Routine, string RelativePath, string AbsolutePath, string Type)>();

                foreach (var r in routines)
                {
                    string subfolder = "procedures";
                    string typeLabel = "StoredProcedure";
                    if (r.Type.Contains("FUNCTION"))
                    {
                        subfolder = "functions";
                        typeLabel = "Function";
                    }
                    else if (r.Type.Contains("TRIGGER"))
                    {
                        subfolder = "triggers";
                        typeLabel = "Trigger";
                    }

                    string relativePath = $"db/{subfolder}/{r.SchemaName}.{r.Name}.sql";
                    string absolutePath = Path.Combine(outputSqlDirectory, subfolder, $"{r.SchemaName}.{r.Name}.sql");
                    string fileId = ComputeHash($"{projectId}:{relativePath}");
                    string entityId = ComputeHash($"{projectId}:{typeLabel}:{r.SchemaName}.{r.Name}");

                    scannedRoutineIds.Add(entityId);
                    routineMetadataList.Add((fileId, entityId, r, relativePath, absolutePath, typeLabel));

                    // Generate a "store" endpoint if naming prefix is matched (usp_data_xxxx_yyyy)
                    if (typeLabel == "StoredProcedure" && r.Name.StartsWith("usp_data_", StringComparison.OrdinalIgnoreCase))
                    {
                        string storeEntityId = ComputeHash($"{projectId}:store:{r.SchemaName}.{r.Name}");
                        scannedRoutineIds.Add(storeEntityId);
                        routineMetadataList.Add((fileId, storeEntityId, r, relativePath, absolutePath, "store"));
                    }
                }

                var existingRoutines = new Dictionary<string, (string FileId, string Name)>();
                string getRoutinesQuery = @"
                    SELECT e.id, e.file_id, e.name 
                    FROM entities e
                    INNER JOIN files f ON e.file_id = f.id
                    WHERE f.project_id = @projectId AND e.type IN ('StoredProcedure', 'Function', 'Trigger', 'store');";

                using (var command = new SqliteCommand(getRoutinesQuery, connection))
                {
                    command.Parameters.AddWithValue("@projectId", projectId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            existingRoutines[reader["id"].ToString()] = (
                                reader["file_id"].ToString(),
                                reader["name"].ToString()
                            );
                        }
                    }
                }

                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var rm in routineMetadataList)
                    {
                        string typeLabel = rm.Type;

                        string dir = Path.GetDirectoryName(rm.AbsolutePath);
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        await File.WriteAllTextAsync(rm.AbsolutePath, rm.Routine.Definition ?? "");

                        string checkFileQuery = "SELECT id FROM files WHERE id = @id;";
                        bool fileExists = false;
                        using (var command = new SqliteCommand(checkFileQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@id", rm.FileId);
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
                                command.Parameters.AddWithValue("@id", rm.FileId);
                                command.Parameters.AddWithValue("@projectId", projectId);
                                command.Parameters.AddWithValue("@relativePath", rm.RelativePath);
                                command.Parameters.AddWithValue("@absolutePath", rm.AbsolutePath);
                                await command.ExecuteNonQueryAsync();
                            }
                        }

                        string checkEntityQuery = "SELECT id, signature, metadata, description FROM entities WHERE id = @id;";
                        string existingSignature = null;
                        string existingMetadata = null;
                        string existingDescription = null;
                        bool entityExists = false;

                        using (var command = new SqliteCommand(checkEntityQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@id", rm.EntityId);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    entityExists = true;
                                    existingSignature = reader["signature"] != DBNull.Value ? reader["signature"].ToString() : null;
                                    existingMetadata = reader["metadata"] != DBNull.Value ? reader["metadata"].ToString() : null;
                                    existingDescription = reader["description"] != DBNull.Value ? reader["description"].ToString() : null;
                                }
                            }
                        }

                        var metadataObj = typeLabel == "store"
                            ? (object)new { code = rm.Routine.Definition, definition = rm.Routine.Definition }
                            : (object)new { definition = rm.Routine.Definition };
                        string metadataJson = JsonSerializer.Serialize(metadataObj);
                        string signature = typeLabel == "store" 
                            ? $"EXECUTE {rm.Routine.SchemaName}.{rm.Routine.Name}" 
                            : $"CREATE {typeLabel.ToUpper()} {rm.Routine.SchemaName}.{rm.Routine.Name}";

                        if (!entityExists)
                        {
                            string insertEntitySql = @"
                                INSERT INTO entities (id, file_id, name, type, signature, start_line, end_line, metadata, description)
                                VALUES (@id, @fileId, @name, @type, @signature, @startLine, @endLine, @metadata, @description);";

                            using (var command = new SqliteCommand(insertEntitySql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", rm.EntityId);
                                command.Parameters.AddWithValue("@fileId", rm.FileId);
                                command.Parameters.AddWithValue("@name", rm.Routine.Name);
                                command.Parameters.AddWithValue("@type", typeLabel);
                                command.Parameters.AddWithValue("@signature", signature);
                                command.Parameters.AddWithValue("@startLine", 1);
                                command.Parameters.AddWithValue("@endLine", (rm.Routine.Definition?.Split('\n').Length ?? 1));
                                command.Parameters.AddWithValue("@metadata", metadataJson);
                                command.Parameters.AddWithValue("@description", (object)rm.Routine.Description ?? DBNull.Value);
                                await command.ExecuteNonQueryAsync();
                            }
                            Console.WriteLine($"[Routine] Inserted routine: {rm.Routine.SchemaName}.{rm.Routine.Name} ({typeLabel})");
                        }
                        else
                        {
                            if (existingSignature != signature || existingMetadata != metadataJson || existingDescription != rm.Routine.Description)
                            {
                                string updateEntitySql = @"
                                    UPDATE entities 
                                    SET signature = @signature, metadata = @metadata, description = @description
                                    WHERE id = @id;";

                                using (var command = new SqliteCommand(updateEntitySql, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@id", rm.EntityId);
                                    command.Parameters.AddWithValue("@signature", signature);
                                    command.Parameters.AddWithValue("@metadata", metadataJson);
                                    command.Parameters.AddWithValue("@description", (object)rm.Routine.Description ?? DBNull.Value);
                                    await command.ExecuteNonQueryAsync();
                                }
                                Console.WriteLine($"[Routine] Updated routine: {rm.Routine.SchemaName}.{rm.Routine.Name} ({typeLabel})");
                            }
                        }
                    }

                    foreach (var erId in existingRoutines.Keys)
                    {
                        if (!scannedRoutineIds.Contains(erId))
                        {
                            var er = existingRoutines[erId];

                            string deleteEntitySql = "DELETE FROM entities WHERE id = @id;";
                            using (var command = new SqliteCommand(deleteEntitySql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", erId);
                                await command.ExecuteNonQueryAsync();
                            }

                            string getFilePathQuery = "SELECT relative_path, absolute_path FROM files WHERE id = @fileId;";
                            string absPath = null;
                            using (var command = new SqliteCommand(getFilePathQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@fileId", er.FileId);
                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        absPath = reader["absolute_path"].ToString();
                                    }
                                }
                            }

                            string deleteFileSql = "DELETE FROM files WHERE id = @id;";
                            using (var command = new SqliteCommand(deleteFileSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", er.FileId);
                                await command.ExecuteNonQueryAsync();
                            }

                            if (!string.IsNullOrEmpty(absPath) && File.Exists(absPath))
                            {
                                try
                                {
                                    File.Delete(absPath);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Warning] Failed to delete file {absPath}: {ex.Message}");
                                }
                            }

                            Console.WriteLine($"[Routine] Deleted routine: {er.Name}");
                        }
                    }

                    await transaction.CommitAsync();
                }
            }
        }
    }

    public class SchemaJson
    {
        public DatabaseSchemaJson database_schema { get; set; }
    }

    public class DatabaseSchemaJson
    {
        public List<TableJson> tables { get; set; }
    }

    public class TableJson
    {
        public string table_name { get; set; }
        public string type { get; set; }
        public List<ColumnJson> columns { get; set; }
    }

    public class ColumnJson
    {
        public string name { get; set; }
        public string type { get; set; }
        public bool? is_primary_key { get; set; }
        public bool? is_foreign_key { get; set; }
        public string references { get; set; }
        public string note { get; set; }
    }
}
