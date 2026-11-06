import * as THREE from 'three';
import { SkinTopology } from './SkinTopology';

export type ShapeType = 'plane' | 'box';

export class SkinMesh {
  mesh: THREE.Mesh;
  geometry: THREE.BufferGeometry;
  normalMaterial: THREE.MeshStandardMaterial;
  wireMaterial: THREE.LineBasicMaterial;

  private wireframe: THREE.LineSegments | null = null;
  private shapeType: ShapeType;

  constructor(topology: SkinTopology, scene: THREE.Scene, shapeType: ShapeType = 'plane') {
    this.shapeType = shapeType;
    this.geometry = new THREE.BufferGeometry();
    this.buildGeometry(topology);

    this.normalMaterial = new THREE.MeshStandardMaterial({
      color: 0xe8b89a,
      side: THREE.DoubleSide,
      roughness: 0.5,
      metalness: 0.0,
    });

    this.mesh = new THREE.Mesh(this.geometry, this.normalMaterial);
    this.mesh.position.z = 0;
    scene.add(this.mesh);

    this.wireMaterial = new THREE.LineBasicMaterial({
      color: 0x444444,
      transparent: true,
      opacity: 0.3,
    });
  }

  buildGeometry(topology: SkinTopology): void {
    const verts = topology.vertices;
    const tris = topology.triangles;

    const positions = new Float32Array(verts.length * 3);
    const uvs = new Float32Array(verts.length * 2);

    for (let i = 0; i < verts.length; i++) {
      positions[i * 3] = verts[i].pos.x;
      positions[i * 3 + 1] = verts[i].pos.y;
      positions[i * 3 + 2] = verts[i].pos.z;

      if (this.shapeType === 'box') {
        const size = topology.boxSize || 1;
        const half = size / 2;
        uvs[i * 2] = (verts[i].pos.x + half) / size;
        uvs[i * 2 + 1] = (verts[i].pos.y + half) / size;
      } else {
        uvs[i * 2] = (verts[i].pos.x + 2) / 4;
        uvs[i * 2 + 1] = (verts[i].pos.y + 2) / 4;
      }
    }

    const index = new Uint32Array(tris.length * 3);
    for (let i = 0; i < tris.length; i++) {
      index[i * 3] = tris[i].v1;
      index[i * 3 + 1] = tris[i].v2;
      index[i * 3 + 2] = tris[i].v3;
    }

    this.geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    this.geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
    this.geometry.setIndex(new THREE.BufferAttribute(index, 1));
    this.geometry.computeVertexNormals();
  }

  rebuild(topology: SkinTopology): void {
    this.geometry.dispose();
    this.geometry = new THREE.BufferGeometry();
    this.buildGeometry(topology);
    this.mesh.geometry = this.geometry;
  }

  updatePositions(topology: SkinTopology): void {
    const posAttr = this.geometry.attributes.position;
    if (!posAttr) return;
    const positions = posAttr.array as Float32Array;
    const verts = topology.vertices;

    for (let i = 0; i < verts.length; i++) {
      positions[i * 3] = verts[i].pos.x;
      positions[i * 3 + 1] = verts[i].pos.y;
      positions[i * 3 + 2] = verts[i].pos.z;
    }
    posAttr.needsUpdate = true;
    this.geometry.computeVertexNormals();
  }
}
