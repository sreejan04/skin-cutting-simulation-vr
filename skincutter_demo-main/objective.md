# Skin Cutter — Interactive 2D Skin-Cutting Prototype

## Goal

A browser-based interactive prototype demonstrating a simple surgical skin-cutting interaction: a flat 2D rectangular surface (skin) in a 3D scene that can be cut with a knife tool and deformed by grabbing vertices.

## Core Interactions

- **Knife mode**: Click and drag across the skin surface to create a visible slit along the knife path.
- **Grab mode**: Click and drag vertices on the skin to deform the surface. Pulling near an incision opens the slit.

## Success Criteria

1. A flat skin-like surface rendered in a 3D scene.
2. Knife mode: drag across the surface → a visible slit appears.
3. The slit remains part of the same overall surface (not two separate objects).
4. Grab mode: grab vertices near the incision and pull.
5. The incision opens and the surrounding surface deforms.

## Architecture

```
src/
├── types.ts                  # Vertex, Triangle, EdgeCrossing interfaces
├── SkinTopology.ts           # Grid init, edge detection, cut application, topology separation
├── SkinMesh.ts               # Three.js BufferGeometry builder + position updater
├── DeformationSystem.ts      # PBD spring solver
├── InteractionManager.ts     # Mouse → raycast → plane → tool routing (grab/knife)
└── main.ts                   # Scene, camera, lights, OrbitControls, tool UI
```

## Technical Approach

- **Library**: Three.js (geometry flexibility, raycaster API, lightweight).
- **Mesh**: 30×30 quad grid (1800 triangles), each quad split into 2 triangles.
- **Cutting**: Edge-crossing detection along the knife path → edge splitting + triangle subdivision → path vertex duplication + side reassignment → non-path shared vertex duplication.
- **Deformation**: Position-based dynamics with spring constraints, 25 iterations per frame.
- **Corner pins**: Four corners are permanently fixed to keep the skin taut.
