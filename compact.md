# Project Handover Context - Session Hand-Off Compact

This document summarizes the exact state of the project at the end of the current session. Reading this file provides immediate alignment with the codebase structure, database schemas, APIs, UI features, and scanner mechanics without needing to inspect code files.

---

## 1. SQLite Database Schema & Triggers (`metadata.db`)

All scanned catalog information is consolidated in `metadata.db` using these tables:

### Schema Tables:
- **`projects`**: Scan profiles (`id`, `parent_id`, `name`, `path`, `type`, `language`, `description`, `scanned_at`).
- **`files`**: Physical file paths (`id`, `project_id`, `relative_path`, `absolute_path`).
- **`db_tables`**: SQL Server tables mapped to projects (`id`, `project_id`, `name`, `schema_name`, `database_name`, `description`, `metadata`).
- **`db_columns`**: Table fields (`id`, `table_id`, `name`, `data_type`, `is_primary_key`, `is_foreign_key`, `is_nullable`, `fk_table`, `fk_column`, `description`).
- **`entities`**: Code items, API endpoints, and template nodes (`id`, `file_id`, `name`, `type`, `signature`, `start_line`, `end_line`, `metadata`, `description`).
- **`relations`**: Connections between elements (`id`, `source_entity_id`, `target_entity_id`, `type`, `metadata`).
- **`fts_entities`**: SQLite FTS5 Virtual Table for full-text search index (`id`, `name`, `type`, `signature`, `description`).

### SQLite FTS5 Auto-Sync Triggers:
We configured three database-level triggers to mirror entity writes into the search index:
- **`trg_entities_after_insert`**: Inserts newly scanned entities into `fts_entities`.
- **`trg_entities_after_update`**: Updates `fts_entities` on code modifications.
- **`trg_entities_after_delete`**: Wipes search records when entities are pruned.

---

## 2. Codebase Scanners Mechanics

### A. C# DotNet Scanner (`HoangTLM.CodeBase.DotNetScanner`):
- **Parser**: Written in C#, uses **Roslyn AST Compiler APIs** to parse C# files statically without building/running the app.
- **Scanned types**: Classes, Interfaces, Enums, Constants, Web API Route Endpoints, RabbitMQ Queues (`QueueDeclare`), and Schedules (Hangfire / Quartz).
- **Prefix Generator**: Checks scanned Stored Procedures; if prefixed with `usp_data_`, it automatically creates an additional `"store"` type endpoint containing the SQL definition.

### B. Angular Frontend Scanner (`HoangTLM.CodeBase.WebScanner`):
- **Parser**: Written in TypeScript/Node.js 18.
- **TS Scanner**: Uses **`ts-morph`** to read component classes (`@Component`, inputs, outputs, template URLs), services, and directives.
- **HTML Scanner**: Uses **`parse5`** with source offsets to index HTML elements, property bindings, and structural directives.
- **Event Linker**: Parses event bindings like `(click)="onAction($event)"` and links them to the target component TS method using a matching record in the `relations` table.

---

## 3. Web API Endpoints (`Program.cs`)

Exposed on **`http://localhost:5080`**:
- `GET /api/projects`: Fetches database scan profiles.
- `GET /api/schema/{projectId}`: Retrieves tables and columns.
- `GET /api/routines/{projectId}`: Fetches SQL Stored Procedures, Functions, and Triggers.
- `GET /api/context/{projectId}`: Fetches static C# and Angular FE codebase context entities.
- `PUT /api/tables/{tableId}/layout`: Saves visual ERD coordinates.
- `PUT /api/tables/{tableId}/description`: Saves table comments.
- `PUT /api/columns/{columnId}/description`: Saves column comments.
- `PUT /api/entities/{entityId}/description`: Saves code/component comments.
- `DELETE /api/projects/{projectId}`: Cascade deletes a project profile and all its files/entities.
- `DELETE /api/tables/{tableId}`: Deletes a database table and its columns.
- `DELETE /api/entities/{entityId}`: Deletes a code entity and its relationship links.

---

## 4. Angular Web Client Dashboard Features

Built on Angular 16 and Vanilla CSS:
- **Left Sidebar Catalog Tree**:
  - **Database Tree**: Table, Schema, Procedures, Triggers, Functions list.
  - **BE Tree**: C# classes, methods, endpoints, queues, schedules.
  - **FE Tree**: Angular components, services, templates, event/property HTML bindings.
- **Central Canvas Workspace**:
  - **Focused ERD View**: Drag-and-drop schema canvas using Foblex Flow. Focuses on the selected table and immediate FK links. Double-click links to delete, drag to connect. Right-click triggers table addition.
  - **Code Viewer**: Renders SQL routine definitions or C#/TS/HTML source code lines when catalog items are selected.
- **Project Manager Dialog**: Opens via the header settings icon to manage and delete scanned profiles.
- **Right Sidebar Documentation & Prune Panel**:
  - Inspects constraints and relationships of tables/columns/entities.
  - Markdown descriptions update handler.
  - **`🚨 Delete Item from Catalog`** button: Instantly deletes the focused table or entity from the catalog index.

---

## 5. Completed Tasks & Current State

1. **Incremental Upsert**: Scanners sync recursively by matching ID hashes, updating code signatures but leaving the manually edited `description` column intact.
2. **VS Code Debugging**: Configurations in `.vscode/launch.json` are fully set up for all projects.
3. **Verification**: Executed dry-run scans indexing 45 C# entities and hundreds of Angular TS/HTML entities and relations. Everything works perfectly.
4. **Git Sync**: Code is clean and pushed to `main` branch.
