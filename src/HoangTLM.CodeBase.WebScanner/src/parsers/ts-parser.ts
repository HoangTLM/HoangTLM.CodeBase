import { Project, SyntaxKind, ClassDeclaration, Decorator } from "ts-morph";
import * as path from "path";
import * as crypto from "crypto";
import { CodeEntity } from "../models/code-entity";

export interface ComponentTemplateInfo {
  htmlPath: string;
  componentId: string;
  className: string;
  namespace: string;
}

export class TsParser {
  private project: Project;

  constructor() {
    this.project = new Project({
      compilerOptions: {
        allowJs: true,
        skipLibCheck: true
      }
    });
  }

  public scanSolution(projectDir: string, projectId: string): { entities: CodeEntity[], templates: ComponentTemplateInfo[] } {
    const entities: CodeEntity[] = [];
    const templates: ComponentTemplateInfo[] = [];

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
        let metadata: any = {
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
        } else if (injectableDecorator) {
          type = "service";
        } else if (directiveDecorator) {
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

  private parseComponentDecorator(decorator: Decorator, componentFilePath: string): { selector?: string, templateUrl?: string } {
    const meta: { selector?: string, templateUrl?: string } = {};
    const args = decorator.getArguments();
    if (args.length === 0) return meta;

    const arg = args[0];
    const objectLiteral = arg.asKind(SyntaxKind.ObjectLiteralExpression);
    if (!objectLiteral) return meta;

    const selectorProp = objectLiteral.getProperty("selector");
    if (selectorProp) {
      const initializer = (selectorProp as any).getInitializer();
      if (initializer) {
        meta.selector = initializer.getText().replace(/['"]/g, "");
      }
    }

    const templateProp = objectLiteral.getProperty("templateUrl");
    if (templateProp) {
      const initializer = (templateProp as any).getInitializer();
      if (initializer) {
        const relativeHtmlPath = initializer.getText().replace(/['"]/g, "");
        // Resolve absolute path to the html file relative to the component file folder
        meta.templateUrl = path.resolve(path.dirname(componentFilePath), relativeHtmlPath).replace(/\\/g, "/");
      }
    }

    return meta;
  }

  private computeHash(input: string): string {
    return crypto.createHash("sha256").update(input).digest("hex");
  }
}
