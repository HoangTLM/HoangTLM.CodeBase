using System;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HoangTLM.CodeBase.DatabaseScanner.Models;
using HoangTLM.CodeBase.DatabaseScanner.SqlServer;
using HoangTLM.CodeBase.DatabaseScanner.Sqlite;

namespace HoangTLM.CodeBase.DatabaseScanner.App
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  HoangTLM.CodeBase.DatabaseScanner Running App  ");
            Console.WriteLine("=================================================");

            try
            {
                // 1. Load Configurations from App.config
                string sqlServerConnString = ConfigurationManager.ConnectionStrings["SqlServerConnectionString"]?.ConnectionString;
                string sqliteConnString = ConfigurationManager.ConnectionStrings["SqliteConnectionString"]?.ConnectionString;

                string projectName = ConfigurationManager.AppSettings["ProjectName"] ?? "SQLServer_Scanner_Project";
                string projectDesc = ConfigurationManager.AppSettings["ProjectDescription"] ?? "Scanned database schema from SQL Server";
                string schemaJsonSetting = ConfigurationManager.AppSettings["SchemaJsonPath"] ?? "schema.json";
                string outputSqlDirectory = ConfigurationManager.AppSettings["OutputSqlDirectory"] ?? "scanned_sql";

                if (string.IsNullOrEmpty(sqlServerConnString))
                {
                    Console.WriteLine("Error: SqlServerConnectionString is not configured in App.config.");
                    return;
                }

                if (string.IsNullOrEmpty(sqliteConnString))
                {
                    Console.WriteLine("Error: SqliteConnectionString is not configured in App.config.");
                    return;
                }

                string databaseName = ParseDatabaseName(sqlServerConnString);

                // 2. Find and resolve schema.json path
                string resolvedSchemaPath = ResolveFilePath(schemaJsonSetting);
                if (resolvedSchemaPath == null)
                {
                    Console.WriteLine($"Error: Could not find schema.json definition starting from {AppDomain.CurrentDomain.BaseDirectory}");
                    return;
                }
                Console.WriteLine($"[Config] Using schema definition: {resolvedSchemaPath}");

                // 3. Create Project Instance
                string projectId = ComputeHash(projectName);
                var project = new Project
                {
                    Id = projectId,
                    Name = projectName,
                    Description = projectDesc,
                    Type = "Database",
                    Language = "SQL",
                    ScannedAt = DateTime.UtcNow,
                    Path = databaseName
                };

                // 4. Initialize Repository and Scanner
                var sqliteRepo = new SqliteRepository(sqliteConnString);
                var sqlScanner = new SqlServerScanner();

                // 5. Initialize SQLite Database
                Console.WriteLine("[Step 1/4] Initializing SQLite database tables...");
                await sqliteRepo.InitializeDatabaseAsync(resolvedSchemaPath);
                Console.WriteLine("SQLite database successfully initialized.");

                // 6. Sync Project metadata
                Console.WriteLine("[Step 2/4] Syncing project metadata...");
                await sqliteRepo.SyncProjectAsync(project);

                // 7. Scan and Sync SQL Server Schema Tables & Columns
                Console.WriteLine($"[Step 3/4] Scanning database '{databaseName}' tables and columns...");
                var (tables, columns) = await sqlScanner.ScanSchemaAsync(sqlServerConnString, project.Id, databaseName);
                Console.WriteLine($"Found {tables.Count} tables and {columns.Count} columns in SQL Server.");

                Console.WriteLine("Syncing schema incrementally into SQLite...");
                await sqliteRepo.SyncSchemaAsync(project.Id, tables, columns);
                Console.WriteLine("Schema sync completed.");

                // 8. Scan and Sync Database Routines (Procedures, Functions, Triggers)
                Console.WriteLine("[Step 4/4] Scanning stored procedures, functions, and triggers...");
                var routines = await sqlScanner.ScanRoutinesAsync(sqlServerConnString);
                Console.WriteLine($"Found {routines.Count} routines in SQL Server.");

                string resolvedSqlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputSqlDirectory);
                Console.WriteLine($"Writing routine SQL files to and syncing with SQLite: {resolvedSqlDir}");
                await sqliteRepo.SyncRoutinesAsync(project.Id, routines, resolvedSqlDir);
                Console.WriteLine("Routine sync completed.");

                Console.WriteLine("=================================================");
                Console.WriteLine("  Scan and incremental sync successfully finished! ");
                Console.WriteLine("=================================================");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[Error] An error occurred during scan process:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
            }
        }

        private static string ParseDatabaseName(string connectionString)
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            if (builder.TryGetValue("Database", out object dbName)) return dbName.ToString();
            if (builder.TryGetValue("Initial Catalog", out object initCat)) return initCat.ToString();
            return "UnknownDB";
        }

        private static string ResolveFilePath(string filename)
        {
            if (Path.IsPathRooted(filename) && File.Exists(filename))
            {
                return filename;
            }

            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                string checkPath = Path.Combine(currentDir, filename);
                if (File.Exists(checkPath))
                {
                    return Path.GetFullPath(checkPath);
                }
                var parent = Directory.GetParent(currentDir);
                if (parent == null) break;
                currentDir = parent.FullName;
            }

            return null;
        }

        private static string ComputeHash(string input)
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
    }
}
