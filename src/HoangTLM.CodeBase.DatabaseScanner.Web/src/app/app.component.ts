import { Component, OnInit, HostListener } from '@angular/core';
import { ApiService, Project, DbTable, DbColumn } from './services/api.service';

interface FlowTable extends DbTable {
  position: { x: number; y: number };
}

interface TreeItem {
  type: 'table' | 'routine';
  id: string;
  name: string;
  data: any;
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  projects: Project[] = [];
  selectedProjectId: string = '';
  
  // Full list of loaded elements
  allTables: FlowTable[] = [];
  routines: any[] = [];
  
  // Displayed elements on the canvas
  displayedTables: FlowTable[] = [];
  connections: any[] = [];
  userAddedTableIds: Set<string> = new Set<string>();

  // Tree Selection
  selectedTreeItem: TreeItem | null = null;
  databaseName: string = 'SQLServer';
  schemaName: string = 'dbo';

  // Sidebar selection & forms
  selectedElementType: 'table' | 'column' | null = null;
  selectedTable: FlowTable | null = null;
  selectedColumn: DbColumn | null = null;
  editDescriptionValue: string = '';

  // New FK Form in right sidebar
  showAddFkForm: boolean = false;
  fkSourceColId: string = '';
  fkTargetTableId: string = '';
  fkTargetColId: string = '';

  // Context Menu
  contextMenu = { visible: false, x: 0, y: 0 };

  // Add Table Modal (Right-click menu action)
  showAddTableModal: boolean = false;
  modalSelectedTableId: string = '';
  modalSourceColId: string = '';
  modalTargetColId: string = '';

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
          this.loadData(this.selectedProjectId);
        }
      },
      error: err => console.error('Failed to load projects', err)
    });
  }

  onProjectChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    this.selectedProjectId = target.value;
    this.loadData(this.selectedProjectId);
    this.clearSelection();
    this.selectedTreeItem = null;
  }

  loadData(projectId: string): void {
    // 1. Load Schema
    this.apiService.getSchema(projectId).subscribe({
      next: res => {
        this.allTables = res.tables.map((t, index) => {
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

        if (this.allTables.length > 0) {
          this.databaseName = this.allTables[0].databaseName || 'SQLServer';
          this.schemaName = this.allTables[0].schemaName || 'dbo';
        }

        // Set default tree selection to the first table if nothing selected
        if (!this.selectedTreeItem && this.allTables.length > 0) {
          this.onTreeItemSelect('table', this.allTables[0]);
        } else {
          this.refreshView();
        }
      },
      error: err => console.error('Failed to load schema', err)
    });

    // 2. Load Routines
    this.apiService.getRoutines(projectId).subscribe({
      next: res => {
        this.routines = res;
      },
      error: err => console.error('Failed to load routines', err)
    });
  }

  getDefaultPosition(index: number): { x: number; y: number } {
    return {
      x: (index % 4) * 350 + 100,
      y: Math.floor(index / 4) * 480 + 100
    };
  }

  getRoutinesByType(type: string): any[] {
    return this.routines.filter(r => r.type === type);
  }

  getRoutineCode(): string {
    if (this.selectedTreeItem?.type === 'routine' && this.selectedTreeItem.data.metadata) {
      try {
        const meta = JSON.parse(this.selectedTreeItem.data.metadata);
        return meta.definition || this.selectedTreeItem.data.signature || '';
      } catch (e) {
        return this.selectedTreeItem.data.signature || '';
      }
    }
    return '';
  }

  // Tree Node Selection
  onTreeItemSelect(type: 'table' | 'routine', item: any): void {
    this.selectedTreeItem = {
      type,
      id: item.id,
      name: item.name,
      data: item
    };

    if (type === 'table') {
      this.userAddedTableIds.clear(); // Reset custom added views
      this.refreshView();
      // Select the table in details editor too
      this.selectElement('table', item);
    } else {
      this.clearSelection();
    }
  }

  // Filter tables to display based on selected table and related tables
  refreshView(): void {
    if (!this.selectedTreeItem || this.selectedTreeItem.type !== 'table') {
      this.displayedTables = [];
      this.connections = [];
      return;
    }

    const selectedTable = this.allTables.find(t => t.id === this.selectedTreeItem!.id);
    if (!selectedTable) return;

    // Collect related tables (connected by FK)
    const relatedTableIds = new Set<string>();
    relatedTableIds.add(selectedTable.id);

    // Add tables manually added in the current view session
    this.userAddedTableIds.forEach(id => relatedTableIds.add(id));

    // Find tables that selectedTable references, or that reference selectedTable
    this.allTables.forEach(t => {
      t.columns.forEach(col => {
        if (col.isForeignKey === 1 && col.fkTable) {
          // If this table references selectedTable
          if (col.fkTable.toLowerCase() === selectedTable.name.toLowerCase() && t.id !== selectedTable.id) {
            relatedTableIds.add(t.id);
          }
          // If selectedTable references this table
          if (t.id === selectedTable.id) {
            const targetTable = this.allTables.find(target => target.name.toLowerCase() === col.fkTable!.toLowerCase());
            if (targetTable) {
              relatedTableIds.add(targetTable.id);
            }
          }
        }
      });
    });

    // Populate displayed tables
    this.displayedTables = this.allTables.filter(t => relatedTableIds.has(t.id));
    this.buildConnections();
  }

  buildConnections(): void {
    this.connections = [];
    this.displayedTables.forEach(table => {
      table.columns.forEach(col => {
        if (col.isForeignKey === 1 && col.fkTable) {
          const targetTable = this.displayedTables.find(t => t.name.toLowerCase() === col.fkTable!.toLowerCase());
          if (targetTable) {
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
    const table = this.allTables.find(t => t.id === tableId);
    if (table) {
      table.position = position;
    }
    this.apiService.saveTableLayout(tableId, position.x, position.y).subscribe({
      next: () => {},
      error: err => console.error('Failed to save layout', err)
    });
  }

  // Handle Drag-to-Connect
  onCreateConnection(event: any): void {
    const sourceColId = event.fOutputId;
    const targetColId = event.fInputId;

    let sourceCol: DbColumn | null = null;
    let sourceTable: FlowTable | null = null;
    for (const t of this.displayedTables) {
      const c = t.columns.find(col => col.id === sourceColId);
      if (c) {
        sourceCol = c;
        sourceTable = t;
        break;
      }
    }

    let targetCol: DbColumn | null = null;
    let targetTable: FlowTable | null = null;
    for (const t of this.displayedTables) {
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
    const confirmDelete = confirm('Are you sure you want to delete this relationship connection?');
    if (confirmDelete) {
      this.apiService.deleteRelationship(conn.sourceColId).subscribe({
        next: () => {
          // Update in allTables and displayedTables
          this.allTables.forEach(t => {
            const c = t.columns.find(col => col.id === conn.sourceColId);
            if (c) {
              c.isForeignKey = 0;
              c.fkTable = null;
              c.fkColumn = null;
            }
          });
          this.refreshView();
          console.log('Relationship deleted.');
        },
        error: err => console.error('Failed to delete relationship', err)
      });
    }
  }

  // Sidebar details selection
  selectElement(type: 'table' | 'column', table: FlowTable, column: DbColumn | null = null): void {
    this.selectedElementType = type;
    this.selectedTable = table;
    this.selectedColumn = column;
    this.editDescriptionValue = type === 'table' ? (table.description || '') : (column?.description || '');
    this.showAddFkForm = false; // Reset FK form when selection changes
  }

  clearSelection(): void {
    this.selectedElementType = null;
    this.selectedTable = null;
    this.selectedColumn = null;
    this.editDescriptionValue = '';
    this.showAddFkForm = false;
  }

  saveDescription(): void {
    if (this.selectedElementType === 'table' && this.selectedTable) {
      const tableId = this.selectedTable.id;
      const desc = this.editDescriptionValue;
      this.apiService.saveTableDescription(tableId, desc).subscribe({
        next: () => {
          this.allTables.find(t => t.id === tableId)!.description = desc;
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
          this.allTables.forEach(t => {
            const c = t.columns.find(col => col.id === columnId);
            if (c) c.description = desc;
          });
          this.selectedColumn!.description = desc;
          alert('Column description updated successfully!');
        },
        error: err => console.error('Failed to update column description', err)
      });
    }
  }

  // get existing FKs for the selected table
  getTableFks(): DbColumn[] {
    if (!this.selectedTable) return [];
    return this.selectedTable.columns.filter(c => c.isForeignKey === 1 && c.fkTable);
  }

  // Triggered when user clicks a foreign key in the sidebar
  selectFkColumn(col: DbColumn): void {
    this.selectElement('column', this.selectedTable!, col);
  }

  // Custom FK addition via Right Sidebar Form
  toggleAddFkForm(): void {
    this.showAddFkForm = !this.showAddFkForm;
    if (this.showAddFkForm && this.selectedTable) {
      this.fkSourceColId = this.selectedTable.columns[0]?.id || '';
      // Set default target table
      const otherTables = this.allTables.filter(t => t.id !== this.selectedTable!.id);
      this.fkTargetTableId = otherTables[0]?.id || '';
      this.onFkTargetTableChange();
    }
  }

  onFkTargetTableChange(): void {
    const targetTable = this.allTables.find(t => t.id === this.fkTargetTableId);
    this.fkTargetColId = targetTable?.columns[0]?.id || '';
  }

  saveCustomFk(): void {
    if (!this.selectedTable || !this.fkSourceColId || !this.fkTargetTableId || !this.fkTargetColId) return;

    const sourceCol = this.selectedTable.columns.find(c => c.id === this.fkSourceColId);
    const targetTable = this.allTables.find(t => t.id === this.fkTargetTableId);
    const targetCol = targetTable?.columns.find(c => c.id === this.fkTargetColId);

    if (sourceCol && targetTable && targetCol) {
      this.apiService.createRelationship(sourceCol.id, targetTable.name, targetCol.name).subscribe({
        next: () => {
          // Update in local models
          this.allTables.forEach(t => {
            const c = t.columns.find(col => col.id === sourceCol.id);
            if (c) {
              c.isForeignKey = 1;
              c.fkTable = targetTable.name;
              c.fkColumn = targetCol.name;
            }
          });
          
          this.refreshView();
          this.showAddFkForm = false;
          alert('Foreign Key relationship created successfully!');
        },
        error: err => console.error('Failed to create relationship', err)
      });
    }
  }

  // Right-Click Context Menu on Canvas
  onCanvasContextMenu(event: MouseEvent): void {
    event.preventDefault();
    if (this.selectedTreeItem?.type === 'table') {
      this.contextMenu.x = event.clientX;
      this.contextMenu.y = event.clientY;
      this.contextMenu.visible = true;
    }
  }

  @HostListener('document:click', [])
  closeContextMenu(): void {
    this.contextMenu.visible = false;
  }

  // Modal to Add Table and link them
  openAddTableModal(): void {
    this.showAddTableModal = true;
    // Find tables that are not currently displayed
    const displayedIds = new Set(this.displayedTables.map(t => t.id));
    const available = this.allTables.filter(t => !displayedIds.has(t.id));
    
    this.modalSelectedTableId = available[0]?.id || '';
    this.modalSourceColId = this.selectedTable?.columns[0]?.id || '';
    
    this.onModalSelectedTableChange();
  }

  getModalAvailableTables(): FlowTable[] {
    const displayedIds = new Set(this.displayedTables.map(t => t.id));
    return this.allTables.filter(t => !displayedIds.has(t.id));
  }

  onModalSelectedTableChange(): void {
    const targetTable = this.allTables.find(t => t.id === this.modalSelectedTableId);
    this.modalTargetColId = targetTable?.columns[0]?.id || '';
  }

  closeAddTableModal(): void {
    this.showAddTableModal = false;
  }

  saveAddTableAndLink(): void {
    if (!this.selectedTable || !this.modalSelectedTableId || !this.modalSourceColId || !this.modalTargetColId) return;

    const sourceCol = this.selectedTable.columns.find(c => c.id === this.modalSourceColId);
    const addedTable = this.allTables.find(t => t.id === this.modalSelectedTableId);
    const targetCol = addedTable?.columns.find(c => c.id === this.modalTargetColId);

    if (sourceCol && addedTable && targetCol) {
      // Create Relationship
      this.apiService.createRelationship(sourceCol.id, addedTable.name, targetCol.name).subscribe({
        next: () => {
          // Update local structures
          this.allTables.forEach(t => {
            const c = t.columns.find(col => col.id === sourceCol.id);
            if (c) {
              c.isForeignKey = 1;
              c.fkTable = addedTable.name;
              c.fkColumn = targetCol.name;
            }
          });

          // Add to current display list
          this.userAddedTableIds.add(addedTable.id);
          this.refreshView();
          this.closeAddTableModal();
          alert(`Table '${addedTable.name}' added to view and linked to '${this.selectedTable!.name}'!`);
        },
        error: err => console.error('Failed to create relationship', err)
      });
    }
  }
}
