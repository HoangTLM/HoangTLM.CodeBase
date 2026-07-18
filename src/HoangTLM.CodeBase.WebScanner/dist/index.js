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
const path = __importStar(require("path"));
const fs = __importStar(require("fs"));
const crypto = __importStar(require("crypto"));
const ts_parser_1 = require("./parsers/ts-parser");
const html_parser_1 = require("./parsers/html-parser");
const db_repo_1 = require("./sqlite/db-repo");
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
        const dbRepo = new db_repo_1.DbRepo(dbPath);
        await dbRepo.init();
        // Ensure Web project exists in the database
        await dbRepo.syncProject(projectId, projectName, targetDir);
        // 1. Scan TypeScript files
        console.log("\n[1/3] Parsing Angular TypeScript AST using ts-morph...");
        const tsParser = new ts_parser_1.TsParser();
        const { entities: tsEntities, templates } = tsParser.scanSolution(targetDir, projectId);
        console.log(`[TsParser] Scanned ${tsEntities.length} TS entities and found ${templates.length} Component HTML templates.`);
        // 2. Scan HTML templates
        console.log("\n[2/3] Parsing Angular HTML templates using parse5...");
        const htmlParser = new html_parser_1.HtmlParser();
        const htmlEntities = [];
        const relations = [];
        for (const template of templates) {
            try {
                const { entities: templateEntities, relations: templateRelations } = htmlParser.scanTemplate(template, projectId, targetDir);
                htmlEntities.push(...templateEntities);
                relations.push(...templateRelations);
            }
            catch (err) {
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
    }
    catch (ex) {
        console.error(`\n[Error] Sync execution failed: ${ex.message}`);
        console.error(ex.stack);
        process.exit(1);
    }
}
main();
