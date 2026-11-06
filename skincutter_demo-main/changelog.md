# Changelog

## v1 — Initial prototype

### Added
- Three.js + Vite + TypeScript project scaffold.
- `SkinTopology` — 30×30 grid initialization, edge-crossing detection, edge splitting + triangle subdivision, path vertex duplication, side-based triangle reassignment, shared non-path vertex duplication.
- `SkinMesh` — Three.js `BufferGeometry` builder with non-indexed position/normal/UV attributes; live position updates for deformation.
- `DeformationSystem` — Position-based dynamics solver with spring constraints (25 iterations/frame); permanent corner pinning (4 corners fixed at origPos).
- `InteractionManager` — Raycast-to-plane mouse projection, grab tool (nearest-vertex pin + drag), knife tool (path collection + cut trigger on mouseup).
- `main.ts` — Scene setup (perspective camera, 3 lights, OrbitControls), animation loop, tool UI buttons (Grab/Knife).
- `index.html` — Minimal HTML shell.

### Engineering decisions
- **Grid-based mesh** (30×30 quads → 1800 triangles) for simplicity and performance.
- **Edge-crossing algorithm** using 2D line-segment intersection tests against all edges per path segment.
- **Topology separation** via vertex duplication + cross-product side classification.
- **Shared vertex breaking** — non-path vertices referenced by both left- and right-side triangles are duplicated to fully isolate the two slit boundaries.
- **OrbitControls** configured to use left-click only through tool modes (grab/knife), right-click for orbit, scroll for zoom.

### Bugfixes (v1)
- Fixed cross-product operand order in 2-path-vertex triangle side detection (`rel.x * pathDir.y - rel.y * pathDir.x` → `pathDir.x * rel.y - pathDir.y * rel.x`) to match `triangleSide`.
- Added `breakSharedVertices()` to duplicate non-path vertices that are shared by both sides of the cut, preventing topological bridging through the grid.
- Added `openSlit()` to automatically offset left and right slit boundaries by 0.04 units perpendicular to the cut direction immediately after cutting, making the incision visible without needing to grab and pull.
- Added Viewport mode (`ToolMode = 'viewport' | 'grab' | 'knife'`) — left-click orbit/pan when active, disabled in grab/knife so tools own left-click. Cursor changes per mode: default (viewport), grab (grab), crosshair (knife).
- Refactored `SkinMesh` to use **indexed geometry** (one position entry per vertex, shared via index buffer) so `computeVertexNormals()` produces smooth shading instead of flat facets.
- Replaced `flatShading: true` with smooth shading on the skin material.
- Added `HemisphereLight` (sky `#ffccaa`, ground `#446688`, intensity 0.6) and a rear `DirectionalLight` (`#88aaff`, 0.4) for balanced lighting from both sides.
- Added **Cube mode** — `SkinTopology.initCube(subdivisions, size)` generates 6 subdivided faces (12×12 per face, 1728 triangles total). Toggle between plane and cube with the Cube button (fresh state each switch, no state carried over).
- Upgraded `edgesIntersect` to **3D closest-points algorithm** for correct edge detection on the cube surface (falls back to 2D test for plane mode).
- Changed `InteractionManager.getMouse()` from plane intersection to `raycaster.intersectObject(mesh)` — works for any mesh shape.
- Updated `DeformationSystem.pinCorners()` to find and pin all 8 cube corners (each corner vertex appears in 3 faces, so 24 vertices pinned total).
- `main.ts` refactored into `initPlane()` / `initCube()` / `toggleShape()` — destroys previous mesh, geometry, and interaction state before creating fresh state.
