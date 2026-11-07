---
name: unity-motion-match
description: Automatically discover and bind existing screen-space Unity uGUI from scene and runtime hierarchies, extract UI templates, and repeatedly match reference motion without a project-specific adapter. Preserves layout and serialized connections; reports unsupported UI and unverified game behavior. Includes optional legacy game profiles.
---

# Unity motion match

Use for replacement reference motion on existing uGUI: translation, uniform scale
and opacity with a fixed camera. Work in an isolated project copy. The default
`auto` path uses one common Editor bridge and generated binding data, not
game-specific C# adapters or private game methods.

## Discover and prepare

Read [automatic binding](references/automatic-binding.md) for selection, outputs
and limits, and [validation scope](references/validation.md) for tested behavior
and remaining visual-fitting errors. For the compositing model, original papers
and a reproducible motion benchmark, read [method and evidence](references/method-and-evidence.md).
The source must compile with the selected Unity Editor and contain
uGUI. Python needs numpy, scipy, opencv-python and Pillow; ffmpeg is needed only
for video input.

```powershell
python scripts/unity.py prepare --source C:/project --workspace C:/motion-work --editor "C:/Program Files/Unity/Hub/Editor/6000.3.21f1/Editor/Unity.exe"
```

This copies Assets/Packages/ProjectSettings without Library, inventories scene
Canvas/RectTransform/layout/component structure, chooses a startup scene, enters
normal Play Mode, discovers resulting UI, selects independent visible groups,
and extracts background/rest/RGBA templates. Repeated launches establish a stable
post-import baseline. No scene is saved and the source is not edited.

Inspect `selection.html`, `auto-report.json`, `auto-bindings.json` and extracted `rest.png` images.
Automatic selection is a structural suggestion, not knowledge of which UI the
user intended to animate. Use `--scene`, `--canvas`, or repeated `--target` data
selectors when the task specifies a screen/group. These need no adapter code.
Duplicate names require an inventory binding ID or a unique path.

For inventory only, use `scan` with the same source/workspace arguments. For a
serialized scene preview without game startup, explicitly choose
`--execution edit`; label it as a preview, not runtime verification. Do not
silently substitute an edit preview when Play Mode initialization fails.

Native Unity may require the host's approved execution path for licensing. Use
existing authorization for the isolated copy. On a failed launch inspect the
log; do not repeatedly retry or repair unrelated game code silently. A prepared
workspace is a snapshot: after changing source UI, create a fresh workspace.

## Match and capture

Place recordings under `REFERENCE_ROOT/CASE-REVISION/`, using case IDs in
`workspace.json`, for example `c_abcd1234-r1/reference.mp4`. Capture uses
two-second clips at 30fps, ending at the original rest pose.

```powershell
python scripts/unity.py match --workspace C:/motion-work --reference C:/references --revision r1
```

The command estimates motion from pixels, writes `solutions/REVISION`, samples
native AnimationClips on bound existing objects, captures Unity output, and
checks preservation. New recordings always require re-extraction. For the same
recording, `scripts/motion.py revise` adjusts speed, axis-specific distance or
stagger; keep a rest-pose key at two seconds when editing timing, and rerun
`unity.py capture --workspace ... --revision ...` after edits.

The pixel fitter uses full-resolution filtered signals and calibrates small
template/raster differences from the reference's final rest frame. Inspect
`*.fit.json` for `referenceRestCalibration`. For a single-color graphic with
opaque pixels, `flatPaintOpacity` records how many frames had enough separate
visible patches to measure the group opacity. A recording that intentionally ends
at another pose/opacity is not a valid input for this workflow.

Auto exports a separate root-relative `.anim` for each target, avoiding ambiguous
Unity animation paths when siblings have identical names. Bindings identify the
actual scene/hierarchy object. No wrapper objects, reparenting, renamed controls
or callback rewiring are introduced.

Inspect output against the reference before calling the motion a match. Passing
layout/binding checks is not proof of visual similarity. Faint pixels, clipping,
similar UI and overlapping opacity can still require correction.

For MP4s, the fitter checks the stationary background for differences introduced
by decoding. On eligible single-color graphics it estimates opacity from the
brightness shared by many strokes. In `*.fit.json`, `videoOpacity.measuredFrames`
counts frames with usable video evidence before `restStartsAt`; other frames
retain the earlier estimate. Inspect overlaps and abrupt transitions in the
replay. A numerical worst-frame bound requires known reference opacity values.

## What the checks establish

Auto compares anchors/pivots/sizes/layout configuration, serialized object
references and persistent event bindings, final pose, button raycast destinations
when available, and restoration at two viewport sizes. It does not invoke
unknown buttons or infer whether purchases, authentication, navigation or other
game-specific outcomes are correct. `gameSpecificSemanticsVerified` stays false.

Run `unity.py audit --workspace ...` for the asset/configuration baseline. Keep
discovery, preparation and replacement costs separate. The previous hand-profile
benchmark does not establish performance for automatic discovery.

Optional `--profile trash-dash` and `--profile survivors-kit` retain earlier
known-game interaction tests. Read [legacy profiles](references/unity-profiles.md)
only when those semantic tests are useful; they are not needed for other uGUI
projects.
