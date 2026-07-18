import { Database, open } from "sqlite";
import * as sqlite3 from "sqlite3";
import { CodeEntity, RelationEntity } from "../models/code-entity";

export class DbRepo {
  private dbPath: string;
  private db: Database | null = null;

  constructor(dbPath: string) {
    this.dbPath = dbPath;
  }

  public async init(): Promise<void> {
    this.db = await open({
      filename: this.dbPath,
      driver: sqlite3.Database
    });
  }

  public async syncProject(
    projectId: string,
    projectName: string,
    projectPath: string
  ): Promise<void> {
    if (!this.db) throw new Error("Database not initialized");

    const check = await this.db.get("SELECT COUNT(*) as count FROM projects WHERE id = ?", projectId);
    if (check.count === 0) {
      await this.db.run(
        `INSERT INTO projects (id, parent_id, name, path, type, language, description)
         VALUES (?, NULL, ?, ?, 'Folder', 'Angular/JS', ?)`,
        [
          projectId,
          projectName,
          projectPath,
          `Generated Angular FE MCP context metadata index for project: ${projectName}`
        ]
      );
      console.log(`[DbRepo] Registered new Web Project in database: ${projectName}`);
    }
  }

  public async syncEntities(
    projectId: string,
    entities: CodeEntity[],
    relations: RelationEntity[]
  ): Promise<void> {
    if (!this.db) throw new Error("Database not initialized");

    await this.db.run("BEGIN TRANSACTION");

    try {
      const scannedEntityIds = new Set<string>();
      const scannedFileIds = new Set<string>();

      // 1. Sync Files and Entities
      for (const entity of entities) {
        scannedEntityIds.add(entity.id);
        scannedFileIds.add(entity.fileId);

        // Ensure file exists
        const fileCheck = await this.db.get("SELECT id FROM files WHERE id = ?", entity.fileId);
        if (!fileCheck) {
          await this.db.run(
            `INSERT INTO files (id, project_id, relative_path, absolute_path) VALUES (?, ?, ?, ?)`,
            [entity.fileId, projectId, entity.relativePath, entity.absolutePath]
          );
        }

        // Incremental Sync (Upsert) Entity
        const entityCheck = await this.db.get(
          "SELECT id, signature, metadata, start_line, end_line FROM entities WHERE id = ?",
          entity.id
        );

        if (!entityCheck) {
          // Fresh Insert
          await this.db.run(
            `INSERT INTO entities (id, file_id, name, type, signature, start_line, end_line, metadata, description)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)`,
            [
              entity.id,
              entity.fileId,
              entity.name,
              entity.type,
              entity.signature,
              entity.startLine,
              entity.endLine,
              entity.metadata
            ]
          );
          console.log(`[DbRepo] Inserted ${entity.type}: ${entity.name}`);
        } else {
          // Check if metadata, signature, or line positions changed, and update if necessary (preserving description!)
          if (
            entityCheck.signature !== entity.signature ||
            entityCheck.metadata !== entity.metadata ||
            entityCheck.start_line !== entity.startLine ||
            entityCheck.end_line !== entity.endLine
          ) {
            await this.db.run(
              `UPDATE entities
               SET signature = ?, metadata = ?, start_line = ?, end_line = ?
               WHERE id = ?`,
              [entity.signature, entity.metadata, entity.startLine, entity.endLine, entity.id]
            );
            console.log(`[DbRepo] Updated ${entity.type}: ${entity.name}`);
          }
        }
      }

      // 2. Sync Relations (Upsert)
      const scannedRelationIds = new Set<string>();
      for (const rel of relations) {
        scannedRelationIds.add(rel.id);

        const relCheck = await this.db.get("SELECT id FROM relations WHERE id = ?", rel.id);
        if (!relCheck) {
          // Verify that both source and target exist to prevent foreign key issues
          const sourceCheck = await this.db.get("SELECT id FROM entities WHERE id = ?", rel.sourceId);
          const targetCheck = await this.db.get("SELECT id FROM entities WHERE id = ?", rel.targetId);

          if (sourceCheck && targetCheck) {
            await this.db.run(
              `INSERT INTO relations (id, source_entity_id, target_entity_id, type, metadata) VALUES (?, ?, ?, ?, ?)`,
              [rel.id, rel.sourceId, rel.targetId, rel.type, rel.metadata]
            );
            console.log(`[DbRepo] Inserted relation [${rel.type}]: ${rel.sourceId} -> ${rel.targetId}`);
          }
        } else {
          await this.db.run(
            `UPDATE relations SET metadata = ? WHERE id = ?`,
            [rel.metadata, rel.id]
          );
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
      
      const existingWebEntities = await this.db.all(
        `SELECT e.id, e.file_id 
         FROM entities e
         INNER JOIN files f ON e.file_id = f.id
         WHERE f.project_id = ? AND e.type IN (${webTypes.map(() => "?").join(",")})`,
        [projectId, ...webTypes]
      );

      for (const entry of existingWebEntities) {
        if (!scannedEntityIds.has(entry.id)) {
          // Delete relations referencing this obsolete entity first
          await this.db.run(
            `DELETE FROM relations WHERE source_entity_id = ? OR target_entity_id = ?`,
            [entry.id, entry.id]
          );
          await this.db.run("DELETE FROM entities WHERE id = ?", entry.id);
          console.log(`[DbRepo] Deleted obsolete entity: ${entry.id}`);
        }
      }

      // 4. Delete Obsolete Relations
      const existingRelations = await this.db.all(
        `SELECT r.id 
         FROM relations r
         INNER JOIN entities e ON r.source_entity_id = e.id
         INNER JOIN files f ON e.file_id = f.id
         WHERE f.project_id = ?`,
        projectId
      );

      for (const rel of existingRelations) {
        if (!scannedRelationIds.has(rel.id)) {
          await this.db.run("DELETE FROM relations WHERE id = ?", rel.id);
          console.log(`[DbRepo] Deleted obsolete relation: ${rel.id}`);
        }
      }

      // 5. Delete obsolete files associations
      const existingFiles = await this.db.all(
        "SELECT id FROM files WHERE project_id = ?",
        projectId
      );

      for (const file of existingFiles) {
        const usage = await this.db.get("SELECT COUNT(*) as count FROM entities WHERE file_id = ?", file.id);
        if (usage.count === 0) {
          await this.db.run("DELETE FROM files WHERE id = ?", file.id);
          console.log(`[DbRepo] Deleted obsolete file association: ${file.id}`);
        }
      }

      await this.db.run("COMMIT");
    } catch (error) {
      await this.db.run("ROLLBACK");
      throw error;
    }
  }

  public async close(): Promise<void> {
    if (this.db) {
      await this.db.close();
    }
  }
}
