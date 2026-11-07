# Automatic uGUI binding

The common bridge contains no game-specific classes, scene paths or target
names. Preparation generates `auto-bindings.json` from actual Unity scene and
runtime objects; it does not generate a new C# adapter for each project.

## Discovery and selection

`scan` opens serialized scenes without saving them and records scene/build
information, screen-space/world-space Canvas types, reference resolutions,
RectTransform hierarchy, component types, layout drivers, active/inactive state,
missing scripts and UI Toolkit presence. A large project is bounded to 64 scenes
with build scenes prioritized; `truncated` makes that limit explicit. `--scene`
scans a specific existing Assets/.../*.unity scene.

`prepare` normally starts the first available enabled build scene. If there is
no valid build entry, it chooses a discovered scene by UI structure. The normal
game startup may load additive scenes and instantiate UI. After its settling
interval, the bridge inventories the resulting runtime Canvases. `--settle`
changes the default three-second minimum (maximum 45). Startup has a bounded
timeout; the bridge never calls guessed private initialization code. Passive-only
startup UI gets a 12-second grace period, and the hierarchy must remain stable
for the settling interval. If default startup times out, preparation tries one
other discovered UI scene in Play Mode and records that fallback. An explicit
`--scene` is never silently replaced.

Automatic cases use one primary root screen-space Canvas by default, selecting up to six
independent visible groups per case. Large full-screen containers are traversed
to find smaller useful groups. A direct layout driver disqualifies a target;
layout-controlled children can remain inside a selected independent parent.
World-space and rotated/reflected hierarchies are not supported.

Useful selection options:

- `--scene Assets/Scenes/Menu.unity`: normal startup scene, or preview scene.
- `--canvas ID_OR_UNIQUE_PATH`: choose a discovered Canvas.
- `--target ID_OR_UNIQUE_PATH`: repeat for independent target groups.
- `--width 960 --height 540`: capture dimensions; otherwise infer CanvasScaler
  aspect ratio, bounded to 960 pixels on the longer side.
- `--max-cases N --max-targets N`: adjust the bounded automatic selection.
- `--include-inactive`: explicitly allow activating an inactive root Canvas for
  capture. It does not guess navigation or activate every inactive child panel.
- `--execution edit`: serialized scene UI only; runtime-created elements and
  game initialization are not tested in this mode.

Binding IDs combine scene, sibling-index hierarchy and display path. A repeated
name alone is rejected if ambiguous. Capture resolves the actual hierarchy and
checks rest geometry against extracted templates; changed/missing targets fail
rather than binding a similarly named object. Use a fresh workspace after
changing the source hierarchy.

`origin=serialized` means an exact serialized object-ID match. Unity can change
prefab instance IDs in Play Mode; `serialized-hierarchy` means only the scene,
sibling indices and name path match the inventory. `runtime-unclassified` has
neither match. `runtimeOnly=true` means observed only in the runtime inventory
of a scanned scene; it does not prove creation, because scripts may rename or
reparent an existing object. These labels do not weaken capture resolution.

## Artifacts

| File | Purpose |
|---|---|
| `scene-inventory.json` | Serialized scene/UI discovery, including unsupported UI |
| `runtime-inventory.json` | UI observed during the selected execution mode |
| `auto-bindings.json` | Selected Canvas/target identities and reasons |
| `auto-report.json` | Supported results, warnings and unverified semantics |
| `selection.html` | Local screenshot with numbered bounds and selector IDs |
| `destination.json`, `fixtures/CASE/*` | Actual template images and geometry |
| `solutions/REVISION/CASE.json` | Reusable sampled motion curves |
| `unity/Assets/MotionMatchGenerated/*.anim` | Native per-target clips; apply at the target root |
| `evidence/REVISION/CASE.json` and frame directories | Native samples/captures and preservation checks |
| `asset-baseline.json`, `asset-integrity.json` | Existing Assets/Packages/ProjectSettings preservation |
| `events.jsonl`, `logs/*`, `auto-error.json` on failure | Execution and error evidence |

The exported clips are root-relative: sample each on its bound target object.
Opacity curves require a CanvasGroup on that target. The capture bridge creates
one temporarily if absent, then restores/removes it. Export does not install a
gameplay controller or save changes to the source scene.

## Boundaries

Unity 6.0+ with uGUI is the current target. UI Toolkit, IMGUI, world-space UI,
arbitrary 3D/camera motion and arbitrary rotations are outside the fitter's scope.
A project must already compile; auto binding is not a universal package upgrade
or missing-script repair system. External services or custom initialization
requirements may prevent normal startup. Inspect discovery and choose a valid
entry scene, or explicitly use an edit preview when that is the intended task.

The capture camera temporarily isolates Canvas, renderer and render-pipeline
state in the copy, then restores it. Scenes are never saved. The workspace has
its own product/save identity; game code that explicitly writes outside Unity's
usual save paths is not sandboxed by this bridge.

Generic checks preserve serialized connections and compare raycast targets;
they do not execute unknown actions. A preserved button binding does not prove a
successful purchase or navigation. Add a separately authorized behavioral test
when that outcome matters; do not relabel structural checks as semantic tests.

The reference must share the extracted UI appearance/background and end at rest.
Color-space metadata follows the project, and the fitter accounts for Linear
blending. Template isolation keeps text/layout components enabled so preferred
sizes do not collapse. The fitter compares every pixel after a small Gaussian
filter, retaining its halo so thin text/rules are not lost by decimation. It
calibrates small raster/alignment differences from the reference's documented
final rest pose (bounded translation and scale), then exports motion relative
to that pose and effective opacity. This does not change scene layout. Raw fits
remain separate for subtracting other targets; static final frames reuse the
calibration. `*.fit.json` records those calibration values.

Matching does not infer correspondence between unrelated designs. Inspect
templates and output; ambiguous or invisible targets need another existing
group/recording, not a hierarchy rewrite.
