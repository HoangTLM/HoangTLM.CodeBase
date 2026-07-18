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
exports.DbRepo = void 0;
const sqlite_1 = require("sqlite");
const sqlite3 = __importStar(require("sqlite3"));
class DbRepo {
    dbPath;
    db = null;
    constructor(dbPath) {
        this.dbPath = dbPath;
    }
    async init() {
        this.db = await (0, sqlite_1.open)({
            filename: this.dbPath,
            driver: sqlite3.Database
        });
    }
    async syncProject(projectId, projectName, projectPath) {
        if (!this.db)
            throw new Error("Database not initialized");
        const check = await this.db.get("SELECT COUNT(*) as count FROM projects WHERE id = ?", projectId);
        if (check.count === 0) {
            await this.db.run(`INSERT INTO projects (id, parent_id, name, path, type, language, description)
         VALUES (?, NULL, ?, ?, 'Folder', 'Angular/JS', ?)`, [
                projectId,
                projectName,
                projectPath,
                `Generated Angular FE MCP context metadata index for project: ${projectName}`
            ]);
            console.log(`[DbRepo] Registered new Web Project in database: ${projectName}`);
        }
    }
    async syncEntities(projectId, entities, relations) {
        if (!this.db)
            throw new Error("Database not initialized");
        await this.db.run("BEGIN TRANSACTION");
        try {
            const scannedEntityIds = new Set();
            const scannedFileIds = new Set();
            // 1. Sync Files and Entities
            for (const entity of entities) {
                scannedEntityIds.add(entity.id);
                scannedFileIds.add(entity.fileId);
                // Ensure file exists
                const fileCheck = await this.db.get("SELECT id FROM files WHERE id = ?", entity.fileId);
                if (!fileCheck) {
                    await this.db.run(`INSERT INTO files (id, project_id, relative_path, absolute_path) VALUES (?, ?, ?, ?)`, [entity.fileId, projectId, entity.relativePath, entity.absolutePath]);
                }
                // Incremental Sync (Upsert) Entity
                const entityCheck = await this.db.get("SELECT id, signature, metadata, start_line, end_line FROM entities WHERE id = ?", entity.id);
                if (!entityCheck) {
                    // Fresh Insert
                    await this.db.run(`INSERT INTO entities (id, file_id, name, type, signature, start_line, end_line, metadata, description)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)`, [
                        entity.id,
                        entity.fileId,
                        entity.name,
                        entity.type,
                        entity.signature,
                        entity.startLine,
                        entity.endLine,
                        entity.metadata
                    ]);
                    console.log(`[DbRepo] Inserted ${entity.type}: ${entity.name}`);
                }
                else {
                    // Check if metadata, signature, or line positions changed, and update if necessary (preserving description!)
                    if (entityCheck.signature !== entity.signature ||
                        entityCheck.metadata !== entity.metadata ||
                        entityCheck.start_line !== entity.startLine ||
                        entityCheck.end_line !== entity.endLine) {
                        await this.db.run(`UPDATE entities
               SET signature = ?, metadata = ?, start_line = ?, end_line = ?
               WHERE id = ?`, [entity.signature, entity.metadata, entity.startLine, entity.endLine, entity.id]);
                        console.log(`[DbRepo] Updated ${entity.type}: ${entity.name}`);
                    }
                }
            }
            // 2. Sync Relations (Upsert)
            const scannedRelationIds = new Set();
            for (const rel of relations) {
                scannedRelationIds.add(rel.id);
                const relCheck = await this.db.get("SELECT id FROM relations WHERE id = ?", rel.id);
                if (!relCheck) {
                    // Verify that both source and target exist to prevent foreign key issues
                    const sourceCheck = await this.db.get("SELECT id FROM entities WHERE id = ?", rel.sourceId);
                    const targetCheck = await this.db.get("SELECT id FROM entities WHERE id = ?", rel.targetId);
                    if (sourceCheck && targetCheck) {
                        await this.db.run(`INSERT INTO relations (id, source_id, target_id, type, metadata) VALUES (?, ?, ?, ?, ?)`, [rel.id, rel.sourceId, rel.targetId, rel.type, rel.metadata]);
                        console.log(`[DbRepo] Inserted relation [${rel.type}]: ${rel.sourceId} -> ${rel.targetId}`);
                    }
                }
                else {
                    await this.db.run(`UPDATE relations SET metadata = ? WHERE id = ?`, [rel.metadata, rel.id]);
                }
            }
            // 3. Delete Obsolete Entities
            const webTypes = [
                "component",
                "service",
                "directive",
                "html-element",
                "event-binding",
                "property-binding",
                "method",
                "interface",
                "enum",
                "class"
            ];
            const existingWebEntities = await this.db.all(`SELECT e.id, e.file_id 
         FROM entities e
         INNER JOIN files f ON e.file_id = f.id
         WHERE f.project_id = ? AND e.type IN (${webTypes.map(() => "?").join(",")})`, [projectId, ...webTypes]);
            for (const entry of existingWebEntities) {
                if (!scannedEntityIds.has(entry.id)) {
                    // Delete relations referencing this obsolete entity first
                    await this.db.run(`DELETE FROM relations WHERE source_id = ? OR target_id = ?`, [entry.id, entry.id]);
                    await this.db.run("DELETE FROM entities WHERE id = ?", entry.id);
                    console.log(`[DbRepo] Deleted obsolete entity: ${entry.id}`);
                }
            }
            // 4. Delete Obsolete Relations
            const existingRelations = await this.db.all(`SELECT r.id 
         FROM relations r
         INNER JOIN entities e ON r.source_id = e.id
         INNER JOIN files f ON e.file_id = f.id
         WHERE f.project_id = ?`, projectId);
            for (const rel of existingRelations) {
                if (!scannedRelationIds.has(rel.id)) {
                    await this.db.run("DELETE FROM relations WHERE id = ?", rel.id);
                    console.log(`[DbRepo] Deleted obsolete relation: ${rel.id}`);
                }
            }
            // 5. Delete obsolete files associations
            const existingFiles = await this.db.all("SELECT id FROM files WHERE project_id = ?", projectId);
            for (const file of existingFiles) {
                const usage = await this.db.get("SELECT COUNT(*) as count FROM entities WHERE file_id = ?", file.id);
                if (usage.count === 0) {
                    await this.db.run("DELETE FROM files WHERE id = ?", file.id);
                    console.log(`[DbRepo] Deleted obsolete file association: ${file.id}`);
                }
            }
            await this.db.run("COMMIT");
        }
        catch (error) {
            await this.db.run("ROLLBACK");
            throw error;
        }
    }
    async close() {
        if (this.db) {
            await this.db.close();
        }
    }
}
exports.DbRepo = DbRepo;
