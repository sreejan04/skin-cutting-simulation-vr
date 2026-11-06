import * as THREE from 'three';
import { Vertex, Triangle, EdgeCrossing } from './types';

export class SkinTopology {
  vertices: Vertex[] = [];
  triangles: Triangle[] = [];
  cutVertexLeft: Set<number> = new Set();
  cutVertexRight: Set<number> = new Set();

  private gridRows: number = 0;
  private gridCols: number = 0;
  isBox: boolean = false;
  boxSize: number = 0;

  initGrid(rows: number, cols: number, width: number, height: number): void {
    this.gridRows = rows;
    this.gridCols = cols;
    this.vertices = [];
    this.triangles = [];
    this.cutVertexLeft.clear();
    this.cutVertexRight.clear();

    const hw = width / 2;
    const hh = height / 2;

    for (let i = 0; i <= rows; i++) {
      for (let j = 0; j <= cols; j++) {
        const x = -hw + (j / cols) * width;
        const y = -hh + (i / rows) * height;
        this.vertices.push({
          pos: new THREE.Vector3(x, y, 0),
          origPos: new THREE.Vector3(x, y, 0),
        });
      }
    }

    for (let i = 0; i < rows; i++) {
      for (let j = 0; j < cols; j++) {
        const v0 = i * (cols + 1) + j;
        const v1 = i * (cols + 1) + j + 1;
        const v2 = (i + 1) * (cols + 1) + j;
        const v3 = (i + 1) * (cols + 1) + j + 1;
        this.triangles.push({ v1: v0, v2: v1, v3: v2 });
        this.triangles.push({ v1: v1, v2: v3, v3: v2 });
      }
    }
  }

  initBox(size: number, subdiv: number): void {
    this.isBox = true;
    this.boxSize = size;
    this.vertices = [];
    this.triangles = [];
    this.cutVertexLeft.clear();
    this.cutVertexRight.clear();

    const half = size / 2;
    const vertMap = new Map<string, number>();

    const getOrCreate = (x: number, y: number, z: number): number => {
      const key = `${x.toFixed(6)},${y.toFixed(6)},${z.toFixed(6)}`;
      const existing = vertMap.get(key);
      if (existing !== undefined) return existing;
      const idx = this.vertices.length;
      this.vertices.push({
        pos: new THREE.Vector3(x, y, z),
        origPos: new THREE.Vector3(x, y, z),
      });
      vertMap.set(key, idx);
      return idx;
    };

    const addFace = (
      uAxis: THREE.Vector3,
      vAxis: THREE.Vector3,
      origin: THREE.Vector3,
      flip: boolean
    ): void => {
      for (let i = 0; i < subdiv; i++) {
        for (let j = 0; j < subdiv; j++) {
          const u0 = origin
            .clone()
            .addScaledVector(uAxis, (j / subdiv) * size)
            .addScaledVector(vAxis, (i / subdiv) * size);
          const u1 = origin
            .clone()
            .addScaledVector(uAxis, ((j + 1) / subdiv) * size)
            .addScaledVector(vAxis, (i / subdiv) * size);
          const u2 = origin
            .clone()
            .addScaledVector(uAxis, (j / subdiv) * size)
            .addScaledVector(vAxis, ((i + 1) / subdiv) * size);
          const u3 = origin
            .clone()
            .addScaledVector(uAxis, ((j + 1) / subdiv) * size)
            .addScaledVector(vAxis, ((i + 1) / subdiv) * size);

          const v0 = getOrCreate(u0.x, u0.y, u0.z);
          const v1 = getOrCreate(u1.x, u1.y, u1.z);
          const v2 = getOrCreate(u2.x, u2.y, u2.z);
          const v3 = getOrCreate(u3.x, u3.y, u3.z);

          if (flip) {
            this.triangles.push({ v1: v0, v2: v2, v3: v1 });
            this.triangles.push({ v1: v1, v2: v2, v3: v3 });
          } else {
            this.triangles.push({ v1: v0, v2: v1, v3: v2 });
            this.triangles.push({ v1: v1, v2: v3, v3: v2 });
          }
        }
      }
    };

    addFace(new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 1, 0), new THREE.Vector3(-half, -half, half), false);
    addFace(new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 1, 0), new THREE.Vector3(-half, -half, -half), true);
    addFace(new THREE.Vector3(0, 0, -1), new THREE.Vector3(0, 1, 0), new THREE.Vector3(half, -half, half), true);
    addFace(new THREE.Vector3(0, 0, 1), new THREE.Vector3(0, 1, 0), new THREE.Vector3(-half, -half, -half), false);
    addFace(new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 0, -1), new THREE.Vector3(-half, half, half), true);
    addFace(new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 0, 1), new THREE.Vector3(-half, -half, -half), false);
  }

  getEdgeKey(a: number, b: number): string {
    return a < b ? `${a}-${b}` : `${b}-${a}`;
  }

  buildEdgeMap(): Map<string, { v1: number; v2: number }> {
    const edgeMap = new Map<string, { v1: number; v2: number }>();
    for (const tri of this.triangles) {
      const edges = [
        [tri.v1, tri.v2],
        [tri.v2, tri.v3],
        [tri.v3, tri.v1],
      ];
      for (const [a, b] of edges) {
        const key = this.getEdgeKey(a, b);
        if (!edgeMap.has(key)) {
          edgeMap.set(key, { v1: a, v2: b });
        }
      }
    }
    return edgeMap;
  }

  private getTriangleEdges(
    tri: Triangle
  ): [number, number][] {
    return [
      [tri.v1, tri.v2],
      [tri.v2, tri.v3],
      [tri.v3, tri.v1],
    ];
  }

  private getTriangleNormal(tri: Triangle): THREE.Vector3 {
    const a = this.vertices[tri.v1].pos;
    const b = this.vertices[tri.v2].pos;
    const c = this.vertices[tri.v3].pos;
    const ab = new THREE.Vector3().subVectors(b, a);
    const ac = new THREE.Vector3().subVectors(c, a);
    const n = new THREE.Vector3().crossVectors(ab, ac);
    if (n.length() < 1e-10) return new THREE.Vector3(0, 0, 1);
    return n.normalize();
  }

  private getVertexNormal(vi: number): THREE.Vector3 {
    const avg = new THREE.Vector3();
    let count = 0;
    for (const tri of this.triangles) {
      if (tri.v1 === vi || tri.v2 === vi || tri.v3 === vi) {
        avg.add(this.getTriangleNormal(tri));
        count++;
      }
    }
    if (count === 0) return new THREE.Vector3(0, 0, 1);
    return avg.divideScalar(count).normalize();
  }

  private getPathSurfaceNormal(pathVertices: number[]): THREE.Vector3 {
    const avg = new THREE.Vector3();
    for (const pv of pathVertices) {
      avg.add(this.getVertexNormal(pv));
    }
    if (avg.length() < 1e-10) return new THREE.Vector3(0, 0, 1);
    return avg.normalize();
  }

  private edgesIntersect(
    p1: THREE.Vector3,
    p2: THREE.Vector3,
    q1: THREE.Vector3,
    q2: THREE.Vector3
  ): { hit: boolean; t: number; u: number } {
    const uVec = new THREE.Vector3().copy(p2).sub(p1);
    const vVec = new THREE.Vector3().copy(q2).sub(q1);
    const w = new THREE.Vector3().copy(p1).sub(q1);

    const a = uVec.dot(uVec);
    const b = uVec.dot(vVec);
    const c = vVec.dot(vVec);
    const d = uVec.dot(w);
    const e = vVec.dot(w);
    const det = a * c - b * b;

    if (Math.abs(det) < 1e-10) {
      const uLen = uVec.length();
      if (uLen < 1e-10 || vVec.length() < 1e-10) return { hit: false, t: 0, u: 0 };
      const uDir = uVec.clone().divideScalar(uLen);
      const q1ToP1 = new THREE.Vector3().copy(p1).sub(q1);
      if (q1ToP1.length() > 1e-8) {
        const cross = new THREE.Vector3().crossVectors(uDir, q1ToP1);
        if (cross.length() > 1e-6) return { hit: false, t: 0, u: 0 };
      }
      const tProj1 = new THREE.Vector3().copy(p1).dot(uDir);
      const tProj2 = new THREE.Vector3().copy(p2).dot(uDir);
      const qProj1 = new THREE.Vector3().copy(q1).dot(uDir);
      const qProj2 = new THREE.Vector3().copy(q2).dot(uDir);
      const lo = Math.max(Math.min(tProj1, tProj2), Math.min(qProj1, qProj2));
      const hi = Math.min(Math.max(tProj1, tProj2), Math.max(qProj1, qProj2));
      if (hi < lo - 1e-6) return { hit: false, t: 0, u: 0 };
      const mid = (lo + hi) / 2;
      const t2 = (mid - tProj1) / uLen;
      const u2 = (mid - qProj1) / uLen;
      return { hit: true, t: Math.max(0, Math.min(1, t2)), u: Math.max(0, Math.min(1, u2)) };
    }

    const s = (b * e - c * d) / det;
    const tVal = (a * e - b * d) / det;

    if (s < -1e-6 || s > 1 + 1e-6 || tVal < -1e-6 || tVal > 1 + 1e-6) {
      return { hit: false, t: 0, u: 0 };
    }

    const pClosest = new THREE.Vector3().copy(p1).addScaledVector(uVec, s);
    const qClosest = new THREE.Vector3().copy(q1).addScaledVector(vVec, tVal);
    if (pClosest.distanceTo(qClosest) > 1e-6) {
      return { hit: false, t: 0, u: 0 };
    }

    return { hit: true, t: s, u: tVal };
  }

  detectCrossings(
    pathPoints: THREE.Vector3[]
  ): EdgeCrossing[] {
    const crossings: EdgeCrossing[] = [];
    const edgeMap = this.buildEdgeMap();

    for (let i = 1; i < pathPoints.length; i++) {
      const A = pathPoints[i - 1];
      const B = pathPoints[i];

      for (const [key, edge] of edgeMap) {
        const v1p = this.vertices[edge.v1].pos;
        const v2p = this.vertices[edge.v2].pos;
        const result = this.edgesIntersect(A, B, v1p, v2p);
        if (result.hit) {
          const px = v1p.x + result.u * (v2p.x - v1p.x);
          const py = v1p.y + result.u * (v2p.y - v1p.y);
          const pz = v1p.z + result.u * (v2p.z - v1p.z);
          crossings.push({
            edgeKey: key,
            v1: edge.v1,
            v2: edge.v2,
            t: result.t + i - 1,
            point: new THREE.Vector3(px, py, pz),
          });
        }
      }
    }

    crossings.sort((a, b) => a.t - b.t);
    const unique: EdgeCrossing[] = [];
    const seen = new Set<string>();
    for (const c of crossings) {
      if (!seen.has(c.edgeKey)) {
        seen.add(c.edgeKey);
        unique.push(c);
      }
    }
    return unique;
  }

  applyCut(crossings: EdgeCrossing[]): void {
    if (crossings.length < 1) return;

    const pathVertices: number[] = [];

    for (const crossing of crossings) {
      const nv = this.vertices.length;
      this.vertices.push({
        pos: crossing.point.clone(),
        origPos: crossing.point.clone(),
      });
      pathVertices.push(nv);

      const newTriangles: Triangle[] = [];
      for (const tri of this.triangles) {
        const edges = this.getTriangleEdges(tri);
        let found = -1;
        for (let ei = 0; ei < 3; ei++) {
          const [a, b] = edges[ei];
          if (this.getEdgeKey(a, b) === crossing.edgeKey) {
            found = ei;
            break;
          }
        }
        if (found === -1) {
          newTriangles.push(tri);
        } else {
          const verts = [tri.v1, tri.v2, tri.v3];
          const va = verts[found];
          const vb = verts[(found + 1) % 3];
          const vc = verts[(found + 2) % 3];
          newTriangles.push({ v1: va, v2: nv, v3: vc });
          newTriangles.push({ v1: nv, v2: vb, v3: vc });
        }
      }
      this.triangles = newTriangles;
    }

    this.separateAlongPath(pathVertices, crossings);
  }

  private separateAlongPath(
    pathVertices: number[],
    crossings: EdgeCrossing[]
  ): void {
    if (pathVertices.length < 1) return;

    const dupMap = new Map<number, number>();
    for (const pv of pathVertices) {
      const dup = this.vertices.length;
      this.vertices.push({
        pos: this.vertices[pv].pos.clone(),
        origPos: this.vertices[pv].origPos.clone(),
      });
      dupMap.set(pv, dup);
    }

    const pathSet = new Set(pathVertices);
    const pathDir = this.computePathDirection(pathVertices);
    const surfaceNormal = this.getPathSurfaceNormal(pathVertices);

    const newTriangles: Triangle[] = [];
    for (const tri of this.triangles) {
      const onPath: number[] = [];
      const offPath: number[] = [];
      for (const v of [tri.v1, tri.v2, tri.v3]) {
        if (pathSet.has(v)) onPath.push(v);
        else offPath.push(v);
      }
      if (onPath.length === 0) {
        newTriangles.push(tri);
      } else if (onPath.length === 1) {
        const pv = onPath[0];
        const side = this.triangleSide(tri, pv, pathDir, surfaceNormal);
        if (side === 'right' && dupMap.has(pv)) {
          const dp = dupMap.get(pv)!;
          const nt: Triangle = { v1: tri.v1, v2: tri.v2, v3: tri.v3 };
          if (nt.v1 === pv) nt.v1 = dp;
          if (nt.v2 === pv) nt.v2 = dp;
          if (nt.v3 === pv) nt.v3 = dp;
          newTriangles.push(nt);
        } else {
          newTriangles.push(tri);
        }
      } else if (onPath.length === 2) {
        const pv1 = onPath[0];
        const pv2 = onPath[1];
        const other = offPath[0];
        const otherPos = this.vertices[other].pos;
        const pv1Pos = this.vertices[pv1].pos;
        const rel = new THREE.Vector3().copy(otherPos).sub(pv1Pos);
        const sideAxis = new THREE.Vector3().crossVectors(pathDir, surfaceNormal).normalize();
        const side = rel.dot(sideAxis) > 0 ? 'right' : 'left';
        if (side === 'right') {
          const dp1 = dupMap.get(pv1)!;
          const dp2 = dupMap.get(pv2)!;
          const nt: Triangle = { v1: tri.v1, v2: tri.v2, v3: tri.v3 };
          if (nt.v1 === pv1) nt.v1 = dp1;
          else if (nt.v1 === pv2) nt.v1 = dp2;
          if (nt.v2 === pv1) nt.v2 = dp1;
          else if (nt.v2 === pv2) nt.v2 = dp2;
          if (nt.v3 === pv1) nt.v3 = dp1;
          else if (nt.v3 === pv2) nt.v3 = dp2;
          newTriangles.push(nt);
        } else {
          newTriangles.push(tri);
        }
      } else {
        const nt: Triangle = { v1: tri.v1, v2: tri.v2, v3: tri.v3 };
        const side = this.triangleSide(
          nt,
          nt.v1,
          pathDir,
          surfaceNormal
        );
        if (side === 'right') {
          if (dupMap.has(nt.v1)) nt.v1 = dupMap.get(nt.v1)!;
          if (dupMap.has(nt.v2)) nt.v2 = dupMap.get(nt.v2)!;
          if (dupMap.has(nt.v3)) nt.v3 = dupMap.get(nt.v3)!;
        }
        newTriangles.push(nt);
      }
    }
    this.triangles = newTriangles;

    for (const pv of pathVertices) {
      if (this.hasIncidentTriangle(pv)) {
        this.cutVertexLeft.add(pv);
      }
      const dp = dupMap.get(pv)!;
      if (this.hasIncidentTriangle(dp)) {
        this.cutVertexRight.add(dp);
      }
    }

    this.breakSharedVertices(pathVertices, dupMap);
    this.openSlit(pathDir, surfaceNormal);
  }

  private openSlit(pathDir: THREE.Vector3, surfaceNormal: THREE.Vector3, gapSize: number = 0.04): void {
    const perp = new THREE.Vector3().crossVectors(pathDir, surfaceNormal);
    if (perp.length() < 1e-8) {
      const up = Math.abs(pathDir.y) < 0.9
        ? new THREE.Vector3(0, 1, 0)
        : new THREE.Vector3(1, 0, 0);
      perp.crossVectors(pathDir, up);
    }
    perp.normalize().multiplyScalar(gapSize / 2);

    for (const vi of this.cutVertexLeft) {
      this.vertices[vi].pos.sub(perp);
    }
    for (const vi of this.cutVertexRight) {
      this.vertices[vi].pos.add(perp);
    }
  }

  private breakSharedVertices(
    pathVertices: number[],
    dupMap: Map<number, number>
  ): void {
    const pathSet = new Set(pathVertices);
    const dupSet = new Set(dupMap.values());

    const triSide = new Int8Array(this.triangles.length);
    for (let i = 0; i < this.triangles.length; i++) {
      const tri = this.triangles[i];
      let hasLeft = false, hasRight = false;
      for (const v of [tri.v1, tri.v2, tri.v3]) {
        if (pathSet.has(v)) hasLeft = true;
        if (dupSet.has(v)) hasRight = true;
      }
      if (hasLeft && !hasRight) triSide[i] = -1;
      else if (hasRight && !hasLeft) triSide[i] = 1;
    }

    const vertSides = new Map<number, { left: boolean; right: boolean }>();
    for (let i = 0; i < this.triangles.length; i++) {
      if (triSide[i] === 0) continue;
      const tri = this.triangles[i];
      const side = triSide[i];
      for (const v of [tri.v1, tri.v2, tri.v3]) {
        if (pathSet.has(v) || dupSet.has(v)) continue;
        let s = vertSides.get(v);
        if (!s) { s = { left: false, right: false }; vertSides.set(v, s); }
        if (side === -1) s.left = true;
        else s.right = true;
      }
    }

    const nonPathDups = new Map<number, number>();
    for (const [vi, sides] of vertSides) {
      if (sides.left && sides.right) {
        const dup = this.vertices.length;
        this.vertices.push({
          pos: this.vertices[vi].pos.clone(),
          origPos: this.vertices[vi].origPos.clone(),
        });
        nonPathDups.set(vi, dup);
      }
    }

    if (nonPathDups.size === 0) return;

    for (let i = 0; i < this.triangles.length; i++) {
      if (triSide[i] !== 1) continue;
      const tri = this.triangles[i];
      if (nonPathDups.has(tri.v1)) tri.v1 = nonPathDups.get(tri.v1)!;
      if (nonPathDups.has(tri.v2)) tri.v2 = nonPathDups.get(tri.v2)!;
      if (nonPathDups.has(tri.v3)) tri.v3 = nonPathDups.get(tri.v3)!;
    }
  }

  private computePathDirection(
    pathVertices: number[]
  ): THREE.Vector3 {
    if (pathVertices.length < 2) {
      return new THREE.Vector3(1, 0, 0);
    }
    const first = this.vertices[pathVertices[0]].pos;
    const last = this.vertices[pathVertices[pathVertices.length - 1]].pos;
    const dir = new THREE.Vector3().copy(last).sub(first);
    if (dir.length() < 1e-8) dir.set(1, 0, 0);
    return dir.normalize();
  }

  private triangleSide(
    tri: Triangle,
    pv: number,
    pathDir: THREE.Vector3,
    surfaceNormal: THREE.Vector3
  ): 'left' | 'right' {
    const other = [tri.v1, tri.v2, tri.v3].find((v) => v !== pv)!;
    const pvPos = this.vertices[pv].pos;
    const otherPos = this.vertices[other].pos;
    const diff = new THREE.Vector3().copy(otherPos).sub(pvPos);
    const sideAxis = new THREE.Vector3().crossVectors(pathDir, surfaceNormal).normalize();
    const dot = diff.dot(sideAxis);
    return dot > 0 ? 'right' : 'left';
  }

  private pointSide(
    point: THREE.Vector3,
    pathDir: THREE.Vector3,
    surfaceNormal: THREE.Vector3
  ): 'left' | 'right' {
    const sideAxis = new THREE.Vector3().crossVectors(pathDir, surfaceNormal).normalize();
    const dot = point.dot(sideAxis);
    return dot > 0 ? 'right' : 'left';
  }

  private hasIncidentTriangle(vi: number): boolean {
    return this.triangles.some(
      (t) => t.v1 === vi || t.v2 === vi || t.v3 === vi
    );
  }
}
