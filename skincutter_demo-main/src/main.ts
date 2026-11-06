import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { SkinTopology } from './SkinTopology';
import { SkinMesh, ShapeType } from './SkinMesh';
import { DeformationSystem } from './DeformationSystem';
import { InteractionManager, ToolMode } from './InteractionManager';

const ROWS = 30;
const COLS = 30;
const WIDTH = 4;
const HEIGHT = 4;
const BOX_SIZE = 3;
const BOX_SUBDIV = 12;

let showBox = false;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1a1a2e);

const camera = new THREE.PerspectiveCamera(45, window.innerWidth / window.innerHeight, 0.1, 20);
camera.position.set(0, 0, 6);
camera.lookAt(0, 0, 0);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
document.body.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.touches = {
  ONE: THREE.TOUCH.PAN,
  TWO: THREE.TOUCH.DOLLY_PAN,
};
controls.enableRotate = true;
controls.enablePan = true;
controls.maxPolarAngle = Math.PI / 2.5;
controls.target.set(0, 0, 0);

const ambientLight = new THREE.AmbientLight(0x404060, 0.6);
scene.add(ambientLight);

const dirLight = new THREE.DirectionalLight(0xffffff, 1.5);
dirLight.position.set(5, 5, 5);
dirLight.castShadow = true;
scene.add(dirLight);

const fillLight = new THREE.DirectionalLight(0xffccaa, 0.5);
fillLight.position.set(-3, -2, 4);
scene.add(fillLight);

const backLight = new THREE.DirectionalLight(0x88aaff, 0.4);
backLight.position.set(-2, -3, -4);
scene.add(backLight);

const hemiLight = new THREE.HemisphereLight(0xffccaa, 0x446688, 0.6);
scene.add(hemiLight);

let topo: SkinTopology;
let skinMesh: SkinMesh;
let deformation: DeformationSystem;
let interaction: InteractionManager;

function initPlane(): void {
  topo = new SkinTopology();
  topo.initGrid(ROWS, COLS, WIDTH, HEIGHT);
  skinMesh = new SkinMesh(topo, scene, 'plane');
  deformation = new DeformationSystem(topo);
  deformation.pinCorners(ROWS, COLS);
  interaction = new InteractionManager(camera, controls, renderer.domElement, topo, skinMesh, deformation);
  interaction.setMode(interaction.mode);
  interaction.onModeChange = () => updateUI();
  updateUI();
}

function initBox(): void {
  topo = new SkinTopology();
  topo.initBox(BOX_SIZE, BOX_SUBDIV);
  skinMesh = new SkinMesh(topo, scene, 'box');
  deformation = new DeformationSystem(topo);
  deformation.pinCorners(0, 0);
  interaction = new InteractionManager(camera, controls, renderer.domElement, topo, skinMesh, deformation);
  interaction.setMode(interaction.mode);
  interaction.onModeChange = () => updateUI();
  updateUI();
}

function toggleShape(): void {
  if (interaction) {
    interaction.destroy();
    scene.remove(skinMesh.mesh);
    skinMesh.geometry.dispose();
  }
  showBox = !showBox;
  if (showBox) {
    initBox();
  } else {
    initPlane();
  }
  shapeBtn.textContent = showBox ? '⬜ Plane' : '📦 Box';
}

window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});

function animate(): void {
  requestAnimationFrame(animate);
  controls.update();

  if (interaction && interaction.isDragging && interaction.mode === 'grab') {
    deformation.step(25);
    skinMesh.updatePositions(topo);
  } else if (interaction && interaction.isDragging && interaction.mode === 'knife') {
    skinMesh.updatePositions(topo);
  }

  renderer.render(scene, camera);
}

animate();

const btnContainer = document.createElement('div');
btnContainer.style.cssText = `
  position: fixed; top: 16px; left: 50%; transform: translateX(-50%);
  display: flex; gap: 8px; z-index: 10;
`;

function makeButton(mode: ToolMode, label: string, active: boolean): HTMLButtonElement {
  const btn = document.createElement('button');
  btn.textContent = label;
  btn.style.cssText = `
    padding: 10px 24px; border: 2px solid #888; border-radius: 8px;
    cursor: pointer; font-size: 15px; font-family: system-ui, sans-serif;
    font-weight: 600; transition: all 0.15s;
    background: ${active ? '#e8b89a' : '#2a2a3e'};
    color: ${active ? '#111' : '#ccc'};
  `;
  btn.onmouseenter = () => {
    if (!active) btn.style.background = '#3a3a5e';
  };
  btn.onmouseleave = () => {
    if (!active) btn.style.background = '#2a2a3e';
  };
  return btn;
}

function updateUI(): void {
  if (!interaction) return;
  btnContainer.innerHTML = '';
  const modes: [ToolMode, string][] = [
    ['viewport', '👁 Viewport'],
    ['grab', '✋ Grab'],
    ['knife', '🔪 Knife'],
  ];
  for (const [mode, label] of modes) {
    const active = interaction.mode === mode;
    const btn = makeButton(mode, label, active);
    btn.onclick = () => interaction.setMode(mode);
    btnContainer.appendChild(btn);
  }
  btnContainer.appendChild(shapeBtn);
}

const shapeBtn = document.createElement('button');
shapeBtn.style.cssText = `
  padding: 10px 24px; border: 2px solid #888; border-radius: 8px;
  cursor: pointer; font-size: 15px; font-family: system-ui, sans-serif;
  font-weight: 600; transition: all 0.15s;
  background: #2a2a3e; color: #ccc; margin-left: 8px;
`;
shapeBtn.onmouseenter = () => { shapeBtn.style.background = '#3a3a5e'; };
shapeBtn.onmouseleave = () => { shapeBtn.style.background = '#2a2a3e'; };
shapeBtn.onclick = () => toggleShape();
shapeBtn.textContent = '📦 Box';
document.body.appendChild(btnContainer);

const info = document.createElement('div');
info.style.cssText = `
  position: fixed; bottom: 16px; left: 50%; transform: translateX(-50%);
  color: #888; font-family: system-ui, sans-serif; font-size: 13px;
  text-align: center; z-index: 10; background: rgba(26,26,46,0.8);
  padding: 8px 16px; border-radius: 6px;
`;
info.textContent = 'Viewport: orbit/pan. Knife: cut. Grab: pull incision. Box button toggles shape.';
document.body.appendChild(info);

initPlane();
