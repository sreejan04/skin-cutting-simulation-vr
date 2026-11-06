import * as THREE from 'three';
import { SkinTopology } from './SkinTopology';

interface Spring {
  v1: number;
  v2: number;
  restLength: number;
}

export class DeformationSystem {
  private springs: Spring[] = [];
  private pinnedVertex: number | null = null;
  private pinTarget: THREE.Vector3 = new THREE.Vector3();
  private permanentPins: Set<number> = new Set();
  private topology: SkinTopology;

  constructor(topology: SkinTopology) {
    this.topology = topology;
    this.buildSprings();
  }

  rebuildSprings(): void {
    this.buildSprings();
  }

  pinCorners(rows: number, cols: number): void {
    this.permanentPins.clear();
    if (this.topology.isBox) {
      const half = this.topology.boxSize / 2;
      const corners = [
        new THREE.Vector3(-half, -half, half),
        new THREE.Vector3( half, -half, half),
        new THREE.Vector3(-half,  half, half),
        new THREE.Vector3( half,  half, half),
        new THREE.Vector3(-half, -half, -half),
        new THREE.Vector3( half, -half, -half),
        new THREE.Vector3(-half,  half, -half),
        new THREE.Vector3( half,  half, -half),
      ];
      const eps = 0.01;
      for (let i = 0; i < this.topology.vertices.length; i++) {
        const pos = this.topology.vertices[i].pos;
        for (const c of corners) {
          if (pos.distanceTo(c) < eps) {
            this.permanentPins.add(i);
            break;
          }
        }
      }
    } else {
      const stride = cols + 1;
      this.permanentPins.add(0);
      this.permanentPins.add(cols);
      this.permanentPins.add(rows * stride);
      this.permanentPins.add(rows * stride + cols);
    }
  }

  private buildSprings(): void {
    this.springs = [];
    const seen = new Set<string>();
    for (const tri of this.topology.triangles) {
      const edges = [
        [tri.v1, tri.v2],
        [tri.v2, tri.v3],
        [tri.v3, tri.v1],
      ];
      for (const [a, b] of edges) {
        const key = a < b ? `${a}-${b}` : `${b}-${a}`;
        if (!seen.has(key)) {
          seen.add(key);
          const restLen = this.topology.vertices[a].pos.distanceTo(
            this.topology.vertices[b].pos
          );
          this.springs.push({ v1: a, v2: b, restLength: restLen });
        }
      }
    }
  }

  pin(vertexIndex: number): void {
    this.pinnedVertex = vertexIndex;
    this.pinTarget.copy(this.topology.vertices[vertexIndex].pos);
  }

  movePin(target: THREE.Vector3): void {
    this.pinTarget.copy(target);
  }

  release(): void {
    this.pinnedVertex = null;
  }

  step(iterations: number = 20): void {
    if (this.pinnedVertex !== null) {
      this.topology.vertices[this.pinnedVertex].pos.copy(this.pinTarget);
    }

    const verts = this.topology.vertices;

    for (let iter = 0; iter < iterations; iter++) {
      for (const spring of this.springs) {
        if (
          this.pinnedVertex !== null &&
          spring.v1 === this.pinnedVertex &&
          spring.v2 === this.pinnedVertex
        )
          continue;
        if (
          this.pinnedVertex !== null &&
          (spring.v1 === this.pinnedVertex || spring.v2 === this.pinnedVertex)
        ) {
          const movable =
            spring.v1 === this.pinnedVertex ? spring.v2 : spring.v1;
          const pinned =
            spring.v1 === this.pinnedVertex ? spring.v1 : spring.v2;
          const pPos = verts[pinned].pos;
          const mPos = verts[movable].pos;
          const diff = new THREE.Vector3().copy(mPos).sub(pPos);
          const dist = diff.length();
          if (dist > 1e-10) {
            const correction = spring.restLength - dist;
            diff.multiplyScalar(correction / dist);
            verts[movable].pos.add(diff);
          }
        } else if (spring.v1 !== spring.v2) {
          const v1Pos = verts[spring.v1].pos;
          const v2Pos = verts[spring.v2].pos;
          const diff = new THREE.Vector3().copy(v2Pos).sub(v1Pos);
          const dist = diff.length();
          if (dist > 1e-10) {
            const correction = spring.restLength - dist;
            diff.multiplyScalar(correction / (dist * 2));
            v1Pos.sub(diff);
            v2Pos.add(diff);
          }
        }
      }
    }

    for (const pi of this.permanentPins) {
      this.topology.vertices[pi].pos.copy(this.topology.vertices[pi].origPos);
    }
  }
}
