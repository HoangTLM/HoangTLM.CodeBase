using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using HoangTLM.CodeBase.DatabaseScanner.Interfaces;
using HoangTLM.CodeBase.DatabaseScanner.Models;

namespace HoangTLM.CodeBase.DatabaseScanner.SqlServer
{
    public class SqlServerScanner : ISqlServerScanner
    {
        public async Task<(List<DbTable> Tables, List<DbColumn> Columns)> ScanSchemaAsync(string connectionString, string projectId, string databaseName)
        {
            var tables = new List<DbTable>();
            var columns = new List<DbColumn>();

            string tablesQuery = @"
                SELECT 
                    t.name AS TableName, 
                    s.name AS SchemaName,
                    DB_NAME() AS DatabaseName,
                    ep.value AS Description
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.extended_properties ep ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
                ORDER BY SchemaName, TableName;";

            string columnsQuery = @"
                SELECT 
                    t.name AS TableName,
                    s.name AS SchemaName,
                    c.name AS ColumnName,
                    tp.name AS DataType,
                    c.max_length AS MaxLength,
                    c.is_nullable AS IsNullable,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM sys.index_columns ic
                        INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        WHERE ic.object_id = t.object_id AND ic.column_id = c.column_id AND i.is_primary_key = 1
                    ) THEN 1 ELSE 0 END AS IsPrimaryKey,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM sys.foreign_key_columns fkc
                        WHERE fkc.parent_object_id = t.object_id AND fkc.parent_column_id = c.column_id
                    ) THEN 1 ELSE 0 END AS IsForeignKey,
                    (SELECT TOP 1 t2.name FROM sys.foreign_key_columns fkc 
                     INNER JOIN sys.objects t2 ON fkc.referenced_object_id = t2.object_id
                     WHERE fkc.parent_object_id = t.object_id AND fkc.parent_column_id = c.column_id) AS FkTable,
                    (SELECT TOP 1 c2.name FROM sys.foreign_key_columns fkc 
                     INNER JOIN sys.columns c2 ON fkc.referenced_object_id = c2.object_id AND fkc.referenced_column_id = c2.column_id
                     WHERE fkc.parent_object_id = t.object_id AND fkc.parent_column_id = c.column_id) AS FkColumn,
                    dc.definition AS DefaultValue,
                    ep.value AS Description
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                LEFT JOIN sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
                ORDER BY SchemaName, TableName, c.column_id;";

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // 1. Fetch tables
                using (var command = new SqlCommand(tablesQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tables.Add(new DbTable
                        {
                            ProjectId = projectId,
                            Name = reader["TableName"].ToString(),
                            SchemaName = reader["SchemaName"].ToString(),
                            DatabaseName = reader["DatabaseName"] != DBNull.Value ? reader["DatabaseName"].ToString() : databaseName,
                            Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null
                        });
                    }
                }

                // 2. Fetch columns
                using (var command = new SqlCommand(columnsQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        columns.Add(new DbColumn
                        {
                            Name = reader["ColumnName"].ToString(),
                            DataType = reader["DataType"].ToString(),
                            MaxLength = reader["MaxLength"] != DBNull.Value ? Convert.ToInt32(reader["MaxLength"]) : (int?)null,
                            IsNullable = Convert.ToInt32(reader["IsNullable"]),
                            IsPrimaryKey = Convert.ToInt32(reader["IsPrimaryKey"]),
                            IsForeignKey = Convert.ToInt32(reader["IsForeignKey"]),
                            FkTable = reader["FkTable"] != DBNull.Value ? reader["FkTable"].ToString() : null,
                            FkColumn = reader["FkColumn"] != DBNull.Value ? reader["FkColumn"].ToString() : null,
                            DefaultVal = reader["DefaultValue"] != DBNull.Value ? reader["DefaultValue"].ToString() : null,
                            Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null,
                            TableName = reader["TableName"].ToString(),
                            SchemaName = reader["SchemaName"].ToString()
                        });
                    }
                }
            }

            return (tables, columns);
        }

        public async Task<List<DbRoutine>> ScanRoutinesAsync(string connectionString)
        {
            var routines = new List<DbRoutine>();

            string routinesQuery = @"
                SELECT 
                    o.name AS Name,
                    o.type_desc AS Type,
                    OBJECT_SCHEMA_NAME(o.object_id) AS SchemaName,
                    m.definition AS Definition,
                    ep.value AS Description
                FROM sys.objects o
                INNER JOIN sys.sql_modules m ON o.object_id = m.object_id
                LEFT JOIN sys.extended_properties ep ON ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
                WHERE o.type IN ('P', 'FN', 'IF', 'TF', 'TR')
                ORDER BY Type, SchemaName, Name;";

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(routinesQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        routines.Add(new DbRoutine
                        {
                            Name = reader["Name"].ToString(),
                            Type = reader["Type"].ToString(),
                            SchemaName = reader["SchemaName"].ToString(),
                            Definition = reader["Definition"] != DBNull.Value ? reader["Definition"].ToString() : null,
                            Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null
                        });
                    }
                }
            }

            return routines;
        }
    }
}
