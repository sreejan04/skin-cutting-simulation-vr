import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { SkinTopology } from './SkinTopology';
import { SkinMesh } from './SkinMesh';
import { DeformationSystem } from './DeformationSystem';

export type ToolMode = 'viewport' | 'grab' | 'knife';

export class InteractionManager {
  mode: ToolMode = 'viewport';
  private raycaster = new THREE.Raycaster();
  private mouse = new THREE.Vector2();
  private camera: THREE.Camera;
  private controls: OrbitControls;
  private domElement: HTMLElement;
  private topology: SkinTopology;
  private skinMesh: SkinMesh;
  private deformation: DeformationSystem;

  isDragging = false;
  private pathPoints: THREE.Vector3[] = [];
  private grabbedVertex: number | null = null;

  onModeChange?: (mode: ToolMode) => void;

  constructor(
    camera: THREE.Camera,
    controls: OrbitControls,
    domElement: HTMLElement,
    topology: SkinTopology,
    skinMesh: SkinMesh,
    deformation: DeformationSystem
  ) {
    this.camera = camera;
    this.controls = controls;
    this.domElement = domElement;
    this.topology = topology;
    this.skinMesh = skinMesh;
    this.deformation = deformation;

    domElement.addEventListener('mousedown', this.onMouseDown);
    domElement.addEventListener('mousemove', this.onMouseMove);
    domElement.addEventListener('mouseup', this.onMouseUp);
    domElement.addEventListener('mouseleave', this.onMouseUp);
  }

  setMode(mode: ToolMode): void {
    this.mode = mode;
    this.isDragging = false;
    this.pathPoints = [];
    this.grabbedVertex = null;
    this.deformation.release();

    if (mode === 'viewport') {
      this.controls.mouseButtons = {
        LEFT: THREE.MOUSE.ROTATE,
        MIDDLE: THREE.MOUSE.DOLLY,
        RIGHT: THREE.MOUSE.PAN,
      };
      document.body.style.cursor = 'default';
    } else {
      this.controls.mouseButtons = {
        LEFT: null,
        MIDDLE: THREE.MOUSE.DOLLY,
        RIGHT: THREE.MOUSE.ROTATE,
      };
      document.body.style.cursor = mode === 'grab' ? 'grab' : 'crosshair';
    }

    this.onModeChange?.(mode);
  }

  private getMouse(event: MouseEvent): THREE.Vector3 {
    const rect = this.domElement.getBoundingClientRect();
    this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

    this.raycaster.setFromCamera(this.mouse, this.camera);
    const intersects = this.raycaster.intersectObject(this.skinMesh.mesh);
    if (intersects.length > 0) {
      return intersects[0].point.clone();
    }
    return new THREE.Vector3();
  }

  private onMouseDown = (event: MouseEvent): void => {
    if (this.mode === 'viewport') return;
    const point = this.getMouse(event);
    this.isDragging = true;

    if (this.mode === 'knife') {
      this.pathPoints = [point];
    } else if (this.mode === 'grab') {
      this.grabbedVertex = this.findClosestVertex(point);
      if (this.grabbedVertex !== null) {
        this.deformation.pin(this.grabbedVertex);
        this.deformation.movePin(point);
      }
    }
  };

  private onMouseMove = (event: MouseEvent): void => {
    if (!this.isDragging) return;
    const point = this.getMouse(event);

    if (this.mode === 'knife') {
      const last = this.pathPoints[this.pathPoints.length - 1];
      if (last.distanceTo(point) > 0.05) {
        this.pathPoints.push(point);
      }
    } else if (this.mode === 'grab' && this.grabbedVertex !== null) {
      this.deformation.movePin(point);
    }
  };

  private onMouseUp = (): void => {
    if (!this.isDragging) return;
    this.isDragging = false;

    if (this.mode === 'knife') {
      this.processCut();
    } else if (this.mode === 'grab') {
      this.grabbedVertex = null;
      this.deformation.release();
    }
  };

  private processCut(): void {
    if (this.pathPoints.length < 2) return;

    const crossings = this.topology.detectCrossings(this.pathPoints);
    if (crossings.length < 1) return;

    this.topology.applyCut(crossings);
    this.skinMesh.rebuild(this.topology);
    this.deformation.rebuildSprings();
    this.pathPoints = [];
  }

  private findClosestVertex(point: THREE.Vector3): number | null {
    let closest: number | null = null;
    let minDist = 0.15;
    for (let i = 0; i < this.topology.vertices.length; i++) {
      const dist = this.topology.vertices[i].pos.distanceTo(point);
      if (dist < minDist) {
        minDist = dist;
        closest = i;
      }
    }
    return closest;
  }

  destroy(): void {
    this.domElement.removeEventListener('mousedown', this.onMouseDown);
    this.domElement.removeEventListener('mousemove', this.onMouseMove);
    this.domElement.removeEventListener('mouseup', this.onMouseUp);
    this.domElement.removeEventListener('mouseleave', this.onMouseUp);
  }
}
