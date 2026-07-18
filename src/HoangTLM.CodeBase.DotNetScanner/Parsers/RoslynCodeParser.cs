using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using HoangTLM.CodeBase.DotNetScanner.Models;

namespace HoangTLM.CodeBase.DotNetScanner.Parsers
{
    public class RoslynCodeParser
    {
        public List<CodeEntity> ScanSolution(string solutionPath, string projectId)
        {
            var entities = new List<CodeEntity>();
            string solutionDir = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDir)) return entities;

            // Find all project files (.csproj)
            var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);

            foreach (var csproj in csprojFiles)
            {
                string projectDir = Path.GetDirectoryName(csproj);
                string projectName = Path.GetFileNameWithoutExtension(csproj);

                var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}Properties{Path.DirectorySeparatorChar}"));

                foreach (var file in csFiles)
                {
                    try
                    {
                        var fileEntities = ScanFile(file, projectId, solutionDir, projectName);
                        entities.AddRange(fileEntities);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Parser Error] Failed to parse file: {file}. Error: {ex.Message}");
                    }
                }
            }

            return entities;
        }

        private List<CodeEntity> ScanFile(string filePath, string projectId, string solutionDir, string projectName)
        {
            var entities = new List<CodeEntity>();
            string codeContent = File.ReadAllText(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(codeContent);
            var root = syntaxTree.GetCompilationUnitRoot();

            string relativePath = Path.GetRelativePath(solutionDir, filePath).Replace('\\', '/');
            string fileId = ComputeHash($"{projectId}:{relativePath}");

            // Extract namespace
            string namespaceName = "Global";
            var namespaceDecl = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceDecl != null)
            {
                namespaceName = namespaceDecl.Name.ToString();
            }

            // 1. Classes & Interfaces
            var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var cls in classDecls)
            {
                var lineSpan = syntaxTree.GetLineSpan(cls.Span);
                string className = cls.Identifier.Text;
                string fullName = string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}";
                string signature = cls.Modifiers.ToString() + " class " + className;
                if (cls.BaseList != null)
                {
                    signature += " : " + string.Join(", ", cls.BaseList.Types);
                }

                // Check if Controller
                bool isController = cls.BaseList?.Types.Any(t => t.ToString().Contains("Controller")) ?? false;
                bool isHostedService = cls.BaseList?.Types.Any(t => t.ToString().Contains("BackgroundService") || t.ToString().Contains("IHostedService")) ?? false;
                bool isRabbitConsumer = cls.BaseList?.Types.Any(t => t.ToString().Contains("IConsumer")) ?? false;

                string type = "class";
                if (isController) type = "controller";
                else if (isHostedService) type = "schedule";
                else if (isRabbitConsumer) type = "queue";

                var metadata = new Dictionary<string, object>
                {
                    { "namespace", namespaceName },
                    { "project", projectName },
                    { "code", cls.ToString() }
                };

                entities.Add(new CodeEntity
                {
                    Id = ComputeHash($"{projectId}:{type}:{fullName}"),
                    FileId = fileId,
                    Name = className,
                    Type = type,
                    Signature = signature,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    Metadata = JsonSerializer.Serialize(metadata),
                    RelativePath = relativePath,
                    AbsolutePath = filePath
                });

                // Extract Controller Actions (Endpoints)
                if (isController)
                {
                    var methods = cls.DescendantNodes().OfType<MethodDeclarationSyntax>()
                        .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)));

                    foreach (var m in methods)
                    {
                        var httpAttr = m.AttributeLists.SelectMany(al => al.Attributes)
                            .FirstOrDefault(a => a.Name.ToString().Contains("Http"));

                        if (httpAttr != null || m.AttributeLists.SelectMany(al => al.Attributes).Any(a => a.Name.ToString().Contains("Route")))
                        {
                            var mSpan = syntaxTree.GetLineSpan(m.Span);
                            string verb = httpAttr != null ? httpAttr.Name.ToString().Replace("Http", "").ToUpper() : "GET";
                            string route = "";

                            var routeAttr = m.AttributeLists.SelectMany(al => al.Attributes)
                                .FirstOrDefault(a => a.Name.ToString().Contains("Route") || a.Name.ToString().Contains("Http"));

                            if (routeAttr?.ArgumentList?.Arguments.Count > 0)
                            {
                                route = routeAttr.ArgumentList.Arguments[0].Expression.ToString().Trim('"');
                            }

                            string endpointName = $"{verb} {route}";
                            var eMeta = new Dictionary<string, object>
                            {
                                { "verb", verb },
                                { "route", route },
                                { "controller", className },
                                { "action", m.Identifier.Text },
                                { "code", m.ToString() }
                            };

                            entities.Add(new CodeEntity
                            {
                                Id = ComputeHash($"{projectId}:endpoint:{fullName}.{m.Identifier.Text}"),
                                FileId = fileId,
                                Name = endpointName,
                                Type = "endpoint",
                                Signature = $"{verb} {route} -> {className}.{m.Identifier.Text}",
                                StartLine = mSpan.StartLinePosition.Line + 1,
                                EndLine = mSpan.EndLinePosition.Line + 1,
                                Metadata = JsonSerializer.Serialize(eMeta),
                                RelativePath = relativePath,
                                AbsolutePath = filePath
                            });
                        }
                    }
                }
            }

            var interfaceDecls = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();
            foreach (var iface in interfaceDecls)
            {
                var lineSpan = syntaxTree.GetLineSpan(iface.Span);
                string ifaceName = iface.Identifier.Text;
                string fullName = string.IsNullOrEmpty(namespaceName) ? ifaceName : $"{namespaceName}.{ifaceName}";
                string signature = iface.Modifiers.ToString() + " interface " + ifaceName;

                var metadata = new Dictionary<string, object>
                {
                    { "namespace", namespaceName },
                    { "project", projectName },
                    { "code", iface.ToString() }
                };

                entities.Add(new CodeEntity
                {
                    Id = ComputeHash($"{projectId}:interface:{fullName}"),
                    FileId = fileId,
                    Name = ifaceName,
                    Type = "interface",
                    Signature = signature,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    Metadata = JsonSerializer.Serialize(metadata),
                    RelativePath = relativePath,
                    AbsolutePath = filePath
                });
            }

            // 2. Methods / Functions (standalone in non-controller classes)
            var allMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var m in allMethods)
            {
                var parentClass = m.Parent as ClassDeclarationSyntax;
                if (parentClass == null) continue;

                // Skip controller actions since they are endpoints
                bool isParentController = parentClass.BaseList?.Types.Any(t => t.ToString().Contains("Controller")) ?? false;
                if (isParentController) continue;

                var mSpan = syntaxTree.GetLineSpan(m.Span);
                string signature = m.Modifiers.ToString() + " " + m.ReturnType.ToString() + " " + m.Identifier.Text + m.ParameterList.ToString();
                string fullName = string.IsNullOrEmpty(namespaceName) 
                    ? $"{parentClass.Identifier.Text}.{m.Identifier.Text}" 
                    : $"{namespaceName}.{parentClass.Identifier.Text}.{m.Identifier.Text}";

                var metadata = new Dictionary<string, object>
                {
                    { "class", parentClass.Identifier.Text },
                    { "namespace", namespaceName },
                    { "code", m.ToString() }
                };

                entities.Add(new CodeEntity
                {
                    Id = ComputeHash($"{projectId}:method:{fullName}"),
                    FileId = fileId,
                    Name = $"{parentClass.Identifier.Text}.{m.Identifier.Text}",
                    Type = "method",
                    Signature = signature,
                    StartLine = mSpan.StartLinePosition.Line + 1,
                    EndLine = mSpan.EndLinePosition.Line + 1,
                    Metadata = JsonSerializer.Serialize(metadata),
                    RelativePath = relativePath,
                    AbsolutePath = filePath
                });
            }

            // 3. Enums
            var enumDecls = root.DescendantNodes().OfType<EnumDeclarationSyntax>();
            foreach (var en in enumDecls)
            {
                var lineSpan = syntaxTree.GetLineSpan(en.Span);
                string enumName = en.Identifier.Text;
                string fullName = string.IsNullOrEmpty(namespaceName) ? enumName : $"{namespaceName}.{enumName}";
                string signature = en.Modifiers.ToString() + " enum " + enumName;

                var metadata = new Dictionary<string, object>
                {
                    { "namespace", namespaceName },
                    { "members", en.Members.Select(m => m.Identifier.Text).ToList() },
                    { "code", en.ToString() }
                };

                entities.Add(new CodeEntity
                {
                    Id = ComputeHash($"{projectId}:enum:{fullName}"),
                    FileId = fileId,
                    Name = enumName,
                    Type = "enum",
                    Signature = signature,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    Metadata = JsonSerializer.Serialize(metadata),
                    RelativePath = relativePath,
                    AbsolutePath = filePath
                });
            }

            // 4. Constants
            var fieldDecls = root.DescendantNodes().OfType<FieldDeclarationSyntax>();
            foreach (var fd in fieldDecls)
            {
                bool isConst = fd.Modifiers.Any(mod => mod.IsKind(SyntaxKind.ConstKeyword));
                if (!isConst) continue;

                var parentClass = fd.Parent as ClassDeclarationSyntax;
                string parentName = parentClass != null ? parentClass.Identifier.Text : "Global";

                var lineSpan = syntaxTree.GetLineSpan(fd.Span);
                foreach (var varDecl in fd.Declaration.Variables)
                {
                    string constName = varDecl.Identifier.Text;
                    string value = varDecl.Initializer?.Value.ToString() ?? "";
                    string signature = $"const {fd.Declaration.Type} {parentName}.{constName} = {value}";
                    string fullName = string.IsNullOrEmpty(namespaceName)
                        ? $"{parentName}.{constName}"
                        : $"{namespaceName}.{parentName}.{constName}";

                    var metadata = new Dictionary<string, object>
                    {
                        { "class", parentName },
                        { "namespace", namespaceName },
                        { "value", value },
                        { "code", fd.ToString() }
                    };

                    entities.Add(new CodeEntity
                    {
                        Id = ComputeHash($"{projectId}:const:{fullName}"),
                        FileId = fileId,
                        Name = constName,
                        Type = "const",
                        Signature = signature,
                        StartLine = lineSpan.StartLinePosition.Line + 1,
                        EndLine = lineSpan.EndLinePosition.Line + 1,
                        Metadata = JsonSerializer.Serialize(metadata),
                        RelativePath = relativePath,
                        AbsolutePath = filePath
                    });
                }
            }

            // 5. Minimal APIs, Queues and Schedules in Program.cs
            if (Path.GetFileName(filePath).Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            {
                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var inv in invocations)
                {
                    var memberAccess = inv.Expression as MemberAccessExpressionSyntax;
                    if (memberAccess == null) continue;

                    string methodName = memberAccess.Name.Identifier.Text;
                    if (methodName.StartsWith("Map") && (methodName.Contains("Get") || methodName.Contains("Post") || methodName.Contains("Put") || methodName.Contains("Delete")))
                    {
                        var lineSpan = syntaxTree.GetLineSpan(inv.Span);
                        string route = "";
                        if (inv.ArgumentList.Arguments.Count > 0)
                        {
                            route = inv.ArgumentList.Arguments[0].Expression.ToString().Trim('"');
                        }

                        string verb = methodName.Replace("Map", "").ToUpper();
                        string endpointName = $"{verb} {route}";

                        var metadata = new Dictionary<string, object>
                        {
                            { "verb", verb },
                            { "route", route },
                            { "code", inv.ToString() }
                        };

                        entities.Add(new CodeEntity
                        {
                            Id = ComputeHash($"{projectId}:endpoint:MinimalApi:{verb}:{route}"),
                            FileId = fileId,
                            Name = endpointName,
                            Type = "endpoint",
                            Signature = $"{verb} {route} (Minimal API)",
                            StartLine = lineSpan.StartLinePosition.Line + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            Metadata = JsonSerializer.Serialize(metadata),
                            RelativePath = relativePath,
                            AbsolutePath = filePath
                        });
                    }

                    // RabbitMQ queue declarations
                    if (methodName.Equals("QueueDeclare", StringComparison.OrdinalIgnoreCase) || methodName.Equals("QueueDeclareNoWait", StringComparison.OrdinalIgnoreCase))
                    {
                        var lineSpan = syntaxTree.GetLineSpan(inv.Span);
                        string queueName = "DynamicQueue";
                        if (inv.ArgumentList.Arguments.Count > 0)
                        {
                            queueName = inv.ArgumentList.Arguments[0].Expression.ToString().Trim('"');
                        }

                        var metadata = new Dictionary<string, object>
                        {
                            { "queueName", queueName },
                            { "code", inv.ToString() }
                        };

                        entities.Add(new CodeEntity
                        {
                            Id = ComputeHash($"{projectId}:queue:{queueName}"),
                            FileId = fileId,
                            Name = queueName,
                            Type = "queue",
                            Signature = $"QueueDeclare: {queueName}",
                            StartLine = lineSpan.StartLinePosition.Line + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            Metadata = JsonSerializer.Serialize(metadata),
                            RelativePath = relativePath,
                            AbsolutePath = filePath
                        });
                    }

                    // Hangfire Schedules
                    if (methodName.Equals("AddOrUpdate", StringComparison.OrdinalIgnoreCase) && memberAccess.Expression.ToString().Contains("RecurringJob"))
                    {
                        var lineSpan = syntaxTree.GetLineSpan(inv.Span);
                        string jobName = "RecurringJob";
                        if (inv.ArgumentList.Arguments.Count > 0)
                        {
                            jobName = inv.ArgumentList.Arguments[0].Expression.ToString().Trim('"');
                        }

                        var metadata = new Dictionary<string, object>
                        {
                            { "jobName", jobName },
                            { "code", inv.ToString() }
                        };

                        entities.Add(new CodeEntity
                        {
                            Id = ComputeHash($"{projectId}:schedule:{jobName}"),
                            FileId = fileId,
                            Name = jobName,
                            Type = "schedule",
                            Signature = $"RecurringJob: {jobName}",
                            StartLine = lineSpan.StartLinePosition.Line + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            Metadata = JsonSerializer.Serialize(metadata),
                            RelativePath = relativePath,
                            AbsolutePath = filePath
                        });
                    }
                }
            }

            return entities;
        }

        private string ComputeHash(string input)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
                return string.Concat(bytes.Select(b => b.ToString("x2")));
            }
        }
    }
}
