using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using HoangTLM.CodeBase.DotNetScanner.Parsers;
using HoangTLM.CodeBase.DotNetScanner.Sqlite;

namespace HoangTLM.CodeBase.DotNetScanner.App
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine(".NET Source Code Context Scanner Booting");
            Console.WriteLine("========================================");

            string connString = ConfigurationManager.AppSettings["SqliteConnectionString"];
            string targetSln = ConfigurationManager.AppSettings["TargetSolutionPath"];

            if (string.IsNullOrEmpty(connString) || string.IsNullOrEmpty(targetSln))
            {
                Console.WriteLine("[Error] Missing configuration values in App.config!");
                return;
            }

            // Resolve relative paths
            string slnFullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), targetSln));
            Console.WriteLine($"[Config] Solution Path: {slnFullPath}");
            
            // Extract DB file path from connection string to resolve relative path
            string dbPath = "";
            var connParts = connString.Split(';');
            foreach (var part in connParts)
            {
                if (part.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                {
                    dbPath = part.Substring("Data Source=".Length);
                    break;
                }
            }
            if (!string.IsNullOrEmpty(dbPath) && !Path.IsPathRooted(dbPath))
            {
                string dbFullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), dbPath));
                connString = $"Data Source={dbFullPath};";
            }
            Console.WriteLine($"[Config] Connection String: {connString}");

            if (!File.Exists(slnFullPath))
            {
                Console.WriteLine($"[Error] Solution file not found at: {slnFullPath}");
                return;
            }

            string projectName = Path.GetFileNameWithoutExtension(slnFullPath);
            string projectId = ComputeHash(projectName);

            Console.WriteLine($"[Config] Active Project: {projectName} (ID: {projectId})");

            try
            {
                // Ensure Database Tables and Triggers are initialized!
                Console.WriteLine("[0/2] Initializing SQLite database schema and triggers...");
                var mainRepo = new HoangTLM.CodeBase.DatabaseScanner.Sqlite.SqliteRepository(connString);
                string schemaJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "schema.json");
                await mainRepo.InitializeDatabaseAsync(schemaJsonPath);

                // Ensure Project exists in SQLite
                await EnsureProjectExistsAsync(connString, projectId, projectName, slnFullPath);

                // Run Parser
                Console.WriteLine("\n[1/2] Parsing C# syntax trees using Roslyn C# Compiler API...");
                var parser = new RoslynCodeParser();
                var entities = parser.ScanSolution(slnFullPath, projectId);
                Console.WriteLine($"[Parser] Discovered {entities.Count} C# code entities (Classes, Methods, Enums, Endpoints, Queues, Schedules).");

                // Sync to Database
                Console.WriteLine("\n[2/2] Synchronizing context metadata to SQLite...");
                var repository = new DotNetSqliteRepository(connString);
                await repository.SyncSolutionEntitiesAsync(projectId, entities);

                Console.WriteLine("\n========================================");
                Console.WriteLine("Scan & Synchronization completed successfully!");
                Console.WriteLine("========================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Error] Sync execution failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static async Task EnsureProjectExistsAsync(string connString, string id, string name, string slnPath)
        {
            using (var connection = new SqliteConnection(connString))
            {
                await connection.OpenAsync();
                
                string checkSql = "SELECT COUNT(*) FROM projects WHERE id = @id;";
                long count = 0;
                using (var cmd = new SqliteCommand(checkSql, connection))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    count = (long)await cmd.ExecuteScalarAsync();
                }

                if (count == 0)
                {
                    string insertSql = @"
                        INSERT INTO projects (id, parent_id, name, path, type, language, description)
                        VALUES (@id, NULL, @name, @path, 'Solution', 'C#', @description);";

                    using (var cmd = new SqliteCommand(insertSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@path", slnPath);
                        cmd.Parameters.AddWithValue("@description", $"Generated C# MCP context metadata index for solution: {name}");
                        await cmd.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine($"[Config] Registered new C# Project in database: {name}");
                }
            }
        }

        private static string ComputeHash(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                return string.Concat(bytes.Select(b => b.ToString("x2")));
            }
        }
    }
}
