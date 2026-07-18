"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.TsParser = void 0;
const ts_morph_1 = require("ts-morph");
const path = __importStar(require("path"));
const crypto = __importStar(require("crypto"));
class TsParser {
    project;
    constructor() {
        this.project = new ts_morph_1.Project({
            compilerOptions: {
                allowJs: true,
                skipLibCheck: true
            }
        });
    }
    scanSolution(projectDir, projectId) {
        const entities = [];
        const templates = [];
        // Add source files recursively excluding node_modules, dist, etc.
        const searchPattern = path.join(projectDir, "src/**/*.ts");
        this.project.addSourceFilesAtPaths(searchPattern);
        const sourceFiles = this.project.getSourceFiles().filter(file => {
            const filePath = file.getFilePath();
            return !filePath.includes("node_modules") &&
                !filePath.includes("dist") &&
                !filePath.endsWith(".spec.ts");
        });
        for (const sourceFile of sourceFiles) {
            const absolutePath = sourceFile.getFilePath();
            const relativePath = path.relative(projectDir, absolutePath).replace(/\\/g, "/");
            const fileId = this.computeHash(`${projectId}:${relativePath}`);
            // Extract namespace (we can use folder structure or mock namespace)
            const folderParts = path.dirname(relativePath).split("/");
            const namespace = folderParts.length > 0 ? folderParts.join(".") : "Global";
            // 1. Scan Classes
            const classes = sourceFile.getClasses();
            for (const cls of classes) {
                const className = cls.getName() || "AnonymousClass";
                const fullName = `${namespace}.${className}`;
                const startLine = cls.getStartLineNumber();
                const endLine = cls.getEndLineNumber();
                // Detect Decorators
                const componentDecorator = cls.getDecorator("Component");
                const injectableDecorator = cls.getDecorator("Injectable");
                const directiveDecorator = cls.getDecorator("Directive");
                let type = "class";
                let metadata = {
                    file: relativePath,
                    className: className,
                    namespace: namespace
                };
                if (componentDecorator) {
                    type = "component";
                    const componentMeta = this.parseComponentDecorator(componentDecorator, absolutePath);
                    metadata = { ...metadata, ...componentMeta };
                    if (componentMeta.templateUrl) {
                        templates.push({
                            htmlPath: componentMeta.templateUrl,
                            componentId: this.computeHash(`${projectId}:${type}:${fullName}`),
                            className: className,
                            namespace: namespace
                        });
                    }
                }
                else if (injectableDecorator) {
                    type = "service";
                }
                else if (directiveDecorator) {
                    type = "directive";
                }
                const signature = cls.getModifiers().map(m => m.getText()).join(" ") + " class " + className;
                metadata.code = cls.getText();
                entities.push({
                    id: this.computeHash(`${projectId}:${type}:${fullName}`),
                    fileId: fileId,
                    name: className,
                    type: type,
                    signature: signature,
                    startLine: startLine,
                    endLine: endLine,
                    metadata: JSON.stringify(metadata),
                    relativePath: relativePath,
                    absolutePath: absolutePath
                });
                // 2. Scan Methods inside classes (specifically component/service methods)
                const methods = cls.getMethods();
                for (const method of methods) {
                    const methodName = method.getName();
                    const mStart = method.getStartLineNumber();
                    const mEnd = method.getEndLineNumber();
                    const mSignature = method.getModifiers().map(m => m.getText()).join(" ") + " " + method.getName() + method.getParameters().map(p => p.getText()).join(", ");
                    const methodMeta = {
                        class: className,
                        namespace: namespace,
                        code: method.getText()
                    };
                    entities.push({
                        id: this.computeHash(`${projectId}:method:${namespace}.${className}.${methodName}`),
                        fileId: fileId,
                        name: `${className}.${methodName}`,
                        type: "method",
                        signature: mSignature,
                        startLine: mStart,
                        endLine: mEnd,
                        metadata: JSON.stringify(methodMeta),
                        relativePath: relativePath,
                        absolutePath: absolutePath
                    });
                }
            }
            // 3. Scan Interfaces
            const interfaces = sourceFile.getInterfaces();
            for (const iface of interfaces) {
                const ifaceName = iface.getName();
                const startLine = iface.getStartLineNumber();
                const endLine = iface.getEndLineNumber();
                const signature = "interface " + ifaceName;
                const metadata = {
                    namespace: namespace,
                    code: iface.getText()
                };
                entities.push({
                    id: this.computeHash(`${projectId}:interface:${namespace}.${ifaceName}`),
                    fileId: fileId,
                    name: ifaceName,
                    type: "interface",
                    signature: signature,
                    startLine: startLine,
                    endLine: endLine,
                    metadata: JSON.stringify(metadata),
                    relativePath: relativePath,
                    absolutePath: absolutePath
                });
            }
            // 4. Scan Enums
            const enums = sourceFile.getEnums();
            for (const en of enums) {
                const enumName = en.getName();
                const startLine = en.getStartLineNumber();
                const endLine = en.getEndLineNumber();
                const signature = "enum " + enumName;
                const metadata = {
                    namespace: namespace,
                    members: en.getMembers().map(m => m.getName()),
                    code: en.getText()
                };
                entities.push({
                    id: this.computeHash(`${projectId}:enum:${namespace}.${enumName}`),
                    fileId: fileId,
                    name: enumName,
                    type: "enum",
                    signature: signature,
                    startLine: startLine,
                    endLine: endLine,
                    metadata: JSON.stringify(metadata),
                    relativePath: relativePath,
                    absolutePath: absolutePath
                });
            }
        }
        return { entities, templates };
    }
    parseComponentDecorator(decorator, componentFilePath) {
        const meta = {};
        const args = decorator.getArguments();
        if (args.length === 0)
            return meta;
        const arg = args[0];
        const objectLiteral = arg.asKind(ts_morph_1.SyntaxKind.ObjectLiteralExpression);
        if (!objectLiteral)
            return meta;
        const selectorProp = objectLiteral.getProperty("selector");
        if (selectorProp) {
            const initializer = selectorProp.getInitializer();
            if (initializer) {
                meta.selector = initializer.getText().replace(/['"]/g, "");
            }
        }
        const templateProp = objectLiteral.getProperty("templateUrl");
        if (templateProp) {
            const initializer = templateProp.getInitializer();
            if (initializer) {
                const relativeHtmlPath = initializer.getText().replace(/['"]/g, "");
                // Resolve absolute path to the html file relative to the component file folder
                meta.templateUrl = path.resolve(path.dirname(componentFilePath), relativeHtmlPath).replace(/\\/g, "/");
            }
        }
        return meta;
    }
    computeHash(input) {
        return crypto.createHash("sha256").update(input).digest("hex");
    }
}
exports.TsParser = TsParser;
