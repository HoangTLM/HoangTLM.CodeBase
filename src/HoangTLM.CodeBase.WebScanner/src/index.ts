import * as path from "path";
import * as fs from "fs";
import * as crypto from "crypto";
import { TsParser } from "./parsers/ts-parser";
import { HtmlParser } from "./parsers/html-parser";
import { DbRepo } from "./sqlite/db-repo";
import { CodeEntity, RelationEntity } from "./models/code-entity";

async function main() {
  console.log("==================================================");
  console.log("Angular & Web Source Code Context Scanner Booting");
  console.log("==================================================");

  const configPath = path.join(__dirname, "../config.json");
  if (!fs.existsSync(configPath)) {
    console.error(`[Error] Configuration file not found at: ${configPath}`);
    process.exit(1);
  }

  const config = JSON.parse(fs.readFileSync(configPath, "utf-8"));
  const dbPath = path.resolve(__dirname, "..", config.sqliteDbPath);
  const targetDir = path.resolve(__dirname, "..", config.targetProjectPath);

  console.log(`[Config] SQLite DB: ${dbPath}`);
  console.log(`[Config] Target Web Dir: ${targetDir}`);

  if (!fs.existsSync(targetDir)) {
    console.error(`[Error] Target Web directory not found at: ${targetDir}`);
    process.exit(1);
  }

  const projectName = path.basename(targetDir);
  const projectId = crypto.createHash("sha256").update(projectName).digest("hex");
  console.log(`[Config] Web Project: ${projectName} (ID: ${projectId})`);

  try {
    const dbRepo = new DbRepo(dbPath);
    await dbRepo.init();

    // Ensure Web project exists in the database
    await dbRepo.syncProject(projectId, projectName, targetDir);

    // 1. Scan TypeScript files
    console.log("\n[1/3] Parsing Angular TypeScript AST using ts-morph...");
    const tsParser = new TsParser();
    const { entities: tsEntities, templates } = tsParser.scanSolution(targetDir, projectId);
    console.log(`[TsParser] Scanned ${tsEntities.length} TS entities and found ${templates.length} Component HTML templates.`);

    // 2. Scan HTML templates
    console.log("\n[2/3] Parsing Angular HTML templates using parse5...");
    const htmlParser = new HtmlParser();
    const htmlEntities: CodeEntity[] = [];
    const relations: RelationEntity[] = [];

    for (const template of templates) {
      try {
        const { entities: templateEntities, relations: templateRelations } = 
          htmlParser.scanTemplate(template, projectId, targetDir);
        
        htmlEntities.push(...templateEntities);
        relations.push(...templateRelations);
      } catch (err: any) {
        console.error(`[HtmlParser] Error parsing template ${template.htmlPath}: ${err.message}`);
      }
    }
    console.log(`[HtmlParser] Scanned ${htmlEntities.length} HTML binding elements & created ${relations.length} event relationships.`);

    // Combine TS and HTML entities
    const allEntities = [...tsEntities, ...htmlEntities];

    // 3. Sync to SQLite
    console.log("\n[3/3] Synchronizing Web context metadata to SQLite...");
    await dbRepo.syncEntities(projectId, allEntities, relations);

    await dbRepo.close();

    console.log("\n==================================================");
    console.log("Web Scan & Synchronization completed successfully!");
    console.log("==================================================");
  } catch (ex: any) {
    console.error(`\n[Error] Sync execution failed: ${ex.message}`);
    console.error(ex.stack);
    process.exit(1);
  }
}

main();
