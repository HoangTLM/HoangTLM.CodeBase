import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Project {
  id: string;
  name: string;
  description: string;
  scannedAt: string;
}

export interface DbColumn {
  id: string;
  name: string;
  dataType: string;
  maxLength: number | null;
  isNullable: number;
  isPrimaryKey: number;
  isForeignKey: number;
  fkTable: string | null;
  fkColumn: string | null;
  defaultVal: string | null;
  description: string | null;
}

export interface DbTable {
  id: string;
  name: string;
  schemaName: string;
  databaseName: string;
  description: string | null;
  metadata: string | null; // layout JSON: { x: number, y: number }
  columns: DbColumn[];
}

export interface SchemaResponse {
  tables: DbTable[];
}

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private baseUrl = 'http://localhost:5080/api'; // API port

  constructor(private http: HttpClient) { }

  getProjects(): Observable<Project[]> {
    return this.http.get<Project[]>(`${this.baseUrl}/projects`);
  }

  getSchema(projectId: string): Observable<SchemaResponse> {
    return this.http.get<SchemaResponse>(`${this.baseUrl}/schema/${projectId}`);
  }

  getRoutines(projectId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/routines/${projectId}`);
  }

  getContextEntities(projectId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/context/${projectId}`);
  }

  saveTableLayout(tableId: string, x: number, y: number): Observable<any> {
    const metadata = JSON.stringify({ x, y });
    return this.http.put(`${this.baseUrl}/tables/${tableId}/layout`, { metadata });
  }

  saveTableDescription(tableId: string, description: string): Observable<any> {
    return this.http.put(`${this.baseUrl}/tables/${tableId}/description`, { description });
  }

  saveColumnDescription(columnId: string, description: string): Observable<any> {
    return this.http.put(`${this.baseUrl}/columns/${columnId}/description`, { description });
  }

  createRelationship(sourceColumnId: string, targetTable: string, targetColumn: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/relationships`, { sourceColumnId, targetTable, targetColumn });
  }

  deleteRelationship(columnId: string): Observable<any> {
    return this.http.delete(`${this.baseUrl}/relationships/${columnId}`);
  }
}
