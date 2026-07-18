using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

string GetConnectionString() => app.Configuration.GetConnectionString("SqliteConnectionString") ?? "Data Source=../metadata.db;";

// 1. GET /api/projects
app.MapGet("/api/projects", async () =>
{
    var projects = new List<object>();
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = "SELECT id, name, description, scanned_at FROM projects ORDER BY scanned_at DESC;";
        using (var cmd = new SqliteCommand(query, conn))
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                projects.Add(new
                {
                    Id = reader["id"].ToString(),
                    Name = reader["name"].ToString(),
                    Description = reader["description"] != DBNull.Value ? reader["description"].ToString() : null,
                    ScannedAt = reader["scanned_at"].ToString()
                });
            }
        }
    }
    return Results.Ok(projects);
});

// 2. GET /api/schema/{projectId}
app.MapGet("/api/schema/{projectId}", async (string projectId) =>
{
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();

        string tablesQuery = "SELECT id, name, schema_name, database_name, description, metadata FROM db_tables WHERE project_id = @projectId;";
        var tableList = new List<Dictionary<string, object>>();

        using (var cmd = new SqliteCommand(tablesQuery, conn))
        {
            cmd.Parameters.AddWithValue("@projectId", projectId);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tableList.Add(new Dictionary<string, object>
                    {
                        { "id", reader["id"].ToString() },
                        { "name", reader["name"].ToString() },
                        { "schemaName", reader["schema_name"].ToString() },
                        { "databaseName", reader["database_name"].ToString() },
                        { "description", reader["description"] != DBNull.Value ? reader["description"].ToString() : null },
                        { "metadata", reader["metadata"] != DBNull.Value ? reader["metadata"].ToString() : null },
                        { "columns", new List<object>() }
                    });
                }
            }
        }

        foreach (var t in tableList)
        {
            string tableId = t["id"].ToString();
            string colsQuery = @"
                SELECT id, name, data_type, max_length, is_nullable, is_primary_key, is_foreign_key, fk_table, fk_column, default_val, description
                FROM db_columns WHERE table_id = @tableId ORDER BY name;";

            var columns = (List<object>)t["columns"];

            using (var cmd = new SqliteCommand(colsQuery, conn))
            {
                cmd.Parameters.AddWithValue("@tableId", tableId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        columns.Add(new
                        {
                            Id = reader["id"].ToString(),
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
                        });
                    }
                }
            }
        }

        return Results.Ok(new { Tables = tableList });
    }
});

// 3. PUT /api/tables/{tableId}/layout
app.MapPut("/api/tables/{tableId}/layout", async (string tableId, HttpContext context) =>
{
    using var document = await JsonDocument.ParseAsync(context.Request.Body);
    if (!document.RootElement.TryGetProperty("metadata", out var metadataProp))
    {
        return Results.BadRequest("Missing 'metadata' property.");
    }
    string metadata = metadataProp.GetString();

    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = "UPDATE db_tables SET metadata = @metadata WHERE id = @id;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@metadata", (object)metadata ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", tableId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    return Results.Ok(new { Success = true });
});

// 4. PUT /api/tables/{tableId}/description
app.MapPut("/api/tables/{tableId}/description", async (string tableId, HttpContext context) =>
{
    using var document = await JsonDocument.ParseAsync(context.Request.Body);
    if (!document.RootElement.TryGetProperty("description", out var descProp))
    {
        return Results.BadRequest("Missing 'description' property.");
    }
    string description = descProp.GetString();

    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = "UPDATE db_tables SET description = @description WHERE id = @id;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", tableId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    return Results.Ok(new { Success = true });
});

// 5. PUT /api/columns/{columnId}/description
app.MapPut("/api/columns/{columnId}/description", async (string columnId, HttpContext context) =>
{
    using var document = await JsonDocument.ParseAsync(context.Request.Body);
    if (!document.RootElement.TryGetProperty("description", out var descProp))
    {
        return Results.BadRequest("Missing 'description' property.");
    }
    string description = descProp.GetString();

    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = "UPDATE db_columns SET description = @description WHERE id = @id;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", columnId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    return Results.Ok(new { Success = true });
});

// 5b. PUT /api/entities/{entityId}/description
app.MapPut("/api/entities/{entityId}/description", async (string entityId, HttpContext context) =>
{
    using var document = await JsonDocument.ParseAsync(context.Request.Body);
    if (!document.RootElement.TryGetProperty("description", out var descProp))
    {
        return Results.BadRequest("Missing 'description' property.");
    }
    string description = descProp.GetString();

    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = "UPDATE entities SET description = @description WHERE id = @id;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", entityId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    return Results.Ok(new { Success = true });
});

// 6. POST /api/relationships
app.MapPost("/api/relationships", async (HttpContext context) =>
{
    using var document = await JsonDocument.ParseAsync(context.Request.Body);
    var root = document.RootElement;
    if (!root.TryGetProperty("sourceColumnId", out var srcProp) ||
        !root.TryGetProperty("targetTable", out var targetTableProp) ||
        !root.TryGetProperty("targetColumn", out var targetColumnProp))
    {
        return Results.BadRequest("Missing relationship properties.");
    }

    string sourceColumnId = srcProp.GetString();
    string targetTable = targetTableProp.GetString();
    string targetColumn = targetColumnProp.GetString();

    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = @"
            UPDATE db_columns 
            SET is_foreign_key = 1, fk_table = @targetTable, fk_column = @targetColumn 
            WHERE id = @id;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@targetTable", targetTable);
            cmd.Parameters.AddWithValue("@targetColumn", targetColumn);
            cmd.Parameters.AddWithValue("@id", sourceColumnId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    return Results.Ok(new { Success = true });
});

// 7. DELETE /api/relationships/{columnId}
app.MapDelete("/api/relationships/{columnId}", async (string columnId) =>
{
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = @"
            UPDATE db_columns 
            SET is_foreign_key = 0, fk_table = NULL, fk_column = NULL 
            WHERE id = @id;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@id", columnId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    return Results.Ok(new { Success = true });
});

app.MapGet("/api/routines/{projectId}", async (string projectId) =>
{
    var routines = new List<object>();
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = @"
            SELECT e.id, e.name, e.type, e.signature, e.description, e.metadata
            FROM entities e
            INNER JOIN files f ON e.file_id = f.id
            WHERE f.project_id = @projectId AND e.type IN ('StoredProcedure', 'Function', 'Trigger', 'store')
            ORDER BY e.type, e.name;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@projectId", projectId);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    routines.Add(new
                    {
                        Id = reader["id"].ToString(),
                        Name = reader["name"].ToString(),
                        Type = reader["type"].ToString(),
                        Signature = reader["signature"].ToString(),
                        Description = reader["description"] != DBNull.Value ? reader["description"].ToString() : null,
                        Metadata = reader["metadata"] != DBNull.Value ? reader["metadata"].ToString() : null
                    });
                }
            }
        }
    }
    return Results.Ok(routines);
});

app.MapGet("/api/context/{projectId}", async (string projectId) =>
{
    var entities = new List<object>();
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        string query = @"
            SELECT e.id, e.name, e.type, e.signature, e.start_line, e.end_line, e.metadata, e.description, f.relative_path
            FROM entities e
            INNER JOIN files f ON e.file_id = f.id
            WHERE f.project_id = @projectId AND e.type IN ('class', 'interface', 'method', 'enum', 'const', 'endpoint', 'queue', 'schedule', 'controller', 'component', 'service', 'directive', 'html-element', 'event-binding', 'property-binding')
            ORDER BY e.type, e.name;";
        using (var cmd = new SqliteCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@projectId", projectId);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    entities.Add(new
                    {
                        Id = reader["id"].ToString(),
                        Name = reader["name"].ToString(),
                        Type = reader["type"].ToString(),
                        Signature = reader["signature"].ToString(),
                        StartLine = Convert.ToInt32(reader["start_line"]),
                        EndLine = Convert.ToInt32(reader["end_line"]),
                        Description = reader["description"] != DBNull.Value ? reader["description"].ToString() : null,
                        Metadata = reader["metadata"] != DBNull.Value ? reader["metadata"].ToString() : null,
                        RelativePath = reader["relative_path"].ToString()
                    });
                }
            }
        }
    }
    return Results.Ok(entities);
});

app.MapDelete("/api/projects/{projectId}", async (string projectId) =>
{
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        using (var tx = (SqliteTransaction)await conn.BeginTransactionAsync())
        {
            try
            {
                string deleteRels = @"
                    DELETE FROM relations 
                    WHERE source_entity_id IN (SELECT e.id FROM entities e INNER JOIN files f ON e.file_id = f.id WHERE f.project_id = @projectId)
                       OR target_entity_id IN (SELECT e.id FROM entities e INNER JOIN files f ON e.file_id = f.id WHERE f.project_id = @projectId);";
                using (var cmd = new SqliteCommand(deleteRels, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete entities
                string deleteEntities = "DELETE FROM entities WHERE file_id IN (SELECT id FROM files WHERE project_id = @projectId);";
                using (var cmd = new SqliteCommand(deleteEntities, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete db_columns
                string deleteCols = "DELETE FROM db_columns WHERE table_id IN (SELECT id FROM db_tables WHERE project_id = @projectId);";
                using (var cmd = new SqliteCommand(deleteCols, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete db_tables
                string deleteTables = "DELETE FROM db_tables WHERE project_id = @projectId;";
                using (var cmd = new SqliteCommand(deleteTables, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete files
                string deleteFiles = "DELETE FROM files WHERE project_id = @projectId;";
                using (var cmd = new SqliteCommand(deleteFiles, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete project
                string deleteProject = "DELETE FROM projects WHERE id = @projectId;";
                using (var cmd = new SqliteCommand(deleteProject, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectId);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return Results.Ok(new { message = "Project deleted successfully" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Results.Problem(ex.Message);
            }
        }
    }
});

app.MapDelete("/api/tables/{tableId}", async (string tableId) =>
{
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        using (var tx = (SqliteTransaction)await conn.BeginTransactionAsync())
        {
            try
            {
                // Delete columns
                string deleteCols = "DELETE FROM db_columns WHERE table_id = @tableId;";
                using (var cmd = new SqliteCommand(deleteCols, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@tableId", tableId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete table
                string deleteTable = "DELETE FROM db_tables WHERE id = @tableId;";
                using (var cmd = new SqliteCommand(deleteTable, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@tableId", tableId);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return Results.Ok(new { message = "Table deleted successfully" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Results.Problem(ex.Message);
            }
        }
    }
});

app.MapDelete("/api/entities/{entityId}", async (string entityId) =>
{
    using (var conn = new SqliteConnection(GetConnectionString()))
    {
        await conn.OpenAsync();
        using (var tx = (SqliteTransaction)await conn.BeginTransactionAsync())
        {
            try
            {
                // Delete relations
                string deleteRels = "DELETE FROM relations WHERE source_entity_id = @entityId OR target_entity_id = @entityId;";
                using (var cmd = new SqliteCommand(deleteRels, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@entityId", entityId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete entity
                string deleteEntity = "DELETE FROM entities WHERE id = @entityId;";
                using (var cmd = new SqliteCommand(deleteEntity, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@entityId", entityId);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return Results.Ok(new { message = "Entity deleted successfully" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Results.Problem(ex.Message);
            }
        }
    }
});

app.Run();
