import { Component, OnInit } from '@angular/core';
import { ApiService, Project, DbTable, DbColumn } from './services/api.service';

interface FlowTable extends DbTable {
  position: { x: number; y: number };
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  projects: Project[] = [];
  selectedProjectId: string = '';
  tables: FlowTable[] = [];
  connections: any[] = [];

  // Sidebar selection
  selectedElementType: 'table' | 'column' | null = null;
  selectedTable: FlowTable | null = null;
  selectedColumn: DbColumn | null = null;
  editDescriptionValue: string = '';

  constructor(private apiService: ApiService) {}

  ngOnInit(): void {
    this.loadProjects();
  }

  loadProjects(): void {
    this.apiService.getProjects().subscribe({
      next: data => {
        this.projects = data;
        if (data.length > 0) {
          this.selectedProjectId = data[0].id;
          this.loadSchema(this.selectedProjectId);
        }
      },
      error: err => console.error('Failed to load projects', err)
    });
  }

  onProjectChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    this.selectedProjectId = target.value;
    this.loadSchema(this.selectedProjectId);
    this.clearSelection();
  }

  loadSchema(projectId: string): void {
    this.apiService.getSchema(projectId).subscribe({
      next: res => {
        this.tables = res.tables.map((t, index) => {
          let pos = { x: 0, y: 0 };
          if (t.metadata) {
            try {
              pos = JSON.parse(t.metadata);
            } catch (e) {
              pos = this.getDefaultPosition(index);
            }
          } else {
            pos = this.getDefaultPosition(index);
          }
          return {
            ...t,
            position: pos
          };
        });
        this.buildConnections();
      },
      error: err => console.error('Failed to load schema', err)
    });
  }

  getDefaultPosition(index: number): { x: number; y: number } {
    return {
      x: (index % 4) * 350 + 100,
      y: Math.floor(index / 4) * 480 + 100
    };
  }

  buildConnections(): void {
    this.connections = [];
    this.tables.forEach(table => {
      table.columns.forEach(col => {
        if (col.isForeignKey === 1 && col.fkTable) {
          // Find target table in the same project schema
          const targetTable = this.tables.find(t => t.name.toLowerCase() === col.fkTable!.toLowerCase());
          if (targetTable) {
            // Find target column in target table
            const targetCol = targetTable.columns.find(c => c.name.toLowerCase() === (col.fkColumn || '').toLowerCase());
            if (targetCol) {
              this.connections.push({
                id: `${col.id}-${targetCol.id}`,
                source: col.id,
                target: targetCol.id,
                sourceColId: col.id
              });
            }
          }
        }
      });
    });
  }

  onNodePositionChange(tableId: string, position: { x: number; y: number }): void {
    const table = this.tables.find(t => t.id === tableId);
    if (table) {
      table.position = position;
    }
    this.apiService.saveTableLayout(tableId, position.x, position.y).subscribe({
      next: () => {},
      error: err => console.error('Failed to save layout', err)
    });
  }

  onCreateConnection(event: any): void {
    const sourceColId = event.fOutputId;
    const targetColId = event.fInputId;

    let sourceCol: DbColumn | null = null;
    let sourceTable: FlowTable | null = null;
    for (const t of this.tables) {
      const c = t.columns.find(col => col.id === sourceColId);
      if (c) {
        sourceCol = c;
        sourceTable = t;
        break;
      }
    }

    let targetCol: DbColumn | null = null;
    let targetTable: FlowTable | null = null;
    for (const t of this.tables) {
      const c = t.columns.find(col => col.id === targetColId);
      if (c) {
        targetCol = c;
        targetTable = t;
        break;
      }
    }

    if (sourceCol && sourceTable && targetCol && targetTable) {
      if (sourceTable.id === targetTable.id) {
        alert('Cannot create relationship within the same table.');
        return;
      }

      this.apiService.createRelationship(sourceCol.id, targetTable.name, targetCol.name).subscribe({
        next: () => {
          sourceCol!.isForeignKey = 1;
          sourceCol!.fkTable = targetTable!.name;
          sourceCol!.fkColumn = targetCol!.name;
          this.buildConnections();
          console.log(`Relationship saved from ${sourceTable!.name}.${sourceCol!.name} to ${targetTable!.name}.${targetCol!.name}`);
        },
        error: err => console.error('Failed to create relationship', err)
      });
    }
  }

  onConnectionDoubleClick(conn: any): void {
    const confirmDelete = confirm('Are you sure you want to delete this visual relationship connection?');
    if (confirmDelete) {
      this.apiService.deleteRelationship(conn.sourceColId).subscribe({
        next: () => {
          for (const t of this.tables) {
            const c = t.columns.find(col => col.id === conn.sourceColId);
            if (c) {
              c.isForeignKey = 0;
              c.fkTable = null;
              c.fkColumn = null;
              break;
            }
          }
          this.buildConnections();
          console.log('Relationship deleted.');
        },
        error: err => console.error('Failed to delete relationship', err)
      });
    }
  }

  selectElement(type: 'table' | 'column', table: FlowTable, column: DbColumn | null = null): void {
    this.selectedElementType = type;
    this.selectedTable = table;
    this.selectedColumn = column;
    this.editDescriptionValue = type === 'table' ? (table.description || '') : (column?.description || '');
  }

  clearSelection(): void {
    this.selectedElementType = null;
    this.selectedTable = null;
    this.selectedColumn = null;
    this.editDescriptionValue = '';
  }

  saveDescription(): void {
    if (this.selectedElementType === 'table' && this.selectedTable) {
      const tableId = this.selectedTable.id;
      const desc = this.editDescriptionValue;
      this.apiService.saveTableDescription(tableId, desc).subscribe({
        next: () => {
          this.selectedTable!.description = desc;
          alert('Table description updated successfully!');
        },
        error: err => console.error('Failed to update table description', err)
      });
    } else if (this.selectedElementType === 'column' && this.selectedColumn) {
      const columnId = this.selectedColumn.id;
      const desc = this.editDescriptionValue;
      this.apiService.saveColumnDescription(columnId, desc).subscribe({
        next: () => {
          this.selectedColumn!.description = desc;
          alert('Column description updated successfully!');
        },
        error: err => console.error('Failed to update column description', err)
      });
    }
  }
}
