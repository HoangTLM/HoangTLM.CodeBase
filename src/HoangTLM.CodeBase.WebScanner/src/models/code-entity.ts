export interface CodeEntity {
  id: string;
  fileId: string;
  name: string;
  type: string;
  signature: string;
  startLine: number;
  endLine: number;
  metadata: string; // JSON stringified
  description?: string;
  relativePath: string;
  absolutePath: string;
}

export interface RelationEntity {
  id: string;
  sourceId: string;
  targetId: string;
  type: string;
  metadata: string; // JSON stringified
}
