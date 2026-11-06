import * as THREE from 'three';

export interface Vertex {
  pos: THREE.Vector3;
  origPos: THREE.Vector3;
}

export interface Triangle {
  v1: number;
  v2: number;
  v3: number;
}

export interface EdgeCrossing {
  edgeKey: string;
  v1: number;
  v2: number;
  t: number;
  point: THREE.Vector3;
}
