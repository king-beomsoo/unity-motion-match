# Validation scope — 2026-11-07

Tested with Unity 6000.3.21f1 on Windows. The automatic path uses the same
`assets/auto/AutoBridge.cs` in every project. No project-specific scene names,
classes, target names or private initialization calls were added.

| Input | Automatic binding and preservation | Pixel motion fitting |
|---|---|---|
| [Survivors Roguelike Kit](https://github.com/Roo-Roo-Roo/survivors-roguelike-kit), commit `3f905381d73a34b516030528b8ef6743868b9e52` | Normal Addressables startup to MainMenu; four targets, four button raycast probes; 1,952 baseline files unchanged | After fitter correction, native regression passed: position MAE 0.039 px, max 0.575 px, opacity MAE 0.00567 |
| [UGUI vs UIToolkit](https://github.com/Anthelmed/UGUI-vs-UIToolkit), commit `01e0cfef4d8243e5561047ada161482582585043` | Automatic Play Mode fallback from the empty build scene to the uGUI Main Menu. Both captures passed seven button probes, layout, listeners, references and restoration at 960×540 and 768×648. Asset audit covered 271 files. | All three elements passed the per-element opacity MAE limit of 0.035 in both recordings. Position MAE/max in pixels were 0.452/11.873 for the first recording and 0.241/11.330 for the second. |
| Independent structural fixture | Duplicate names, layout-managed descendants, runtime-created UI and paused time; four targets × 61 samples, six button probes; 40 baseline files unchanged | Exported curves replayed with max numerical error 0.0000113. This tests integration, not arbitrary video recovery |
| [UGUI Animation Samples](https://github.com/annulusgames/UGUIAnimationSamples), commit `343c8110e5683be209cc01ccb4cb986175e61643` | Blocked before discovery by original LitMotion/TextMeshPro compilation errors under the chosen Unity version | Not tested |

Real-project references were controlled animations rendered on the actual
existing UI. The fitter received pixels and extracted templates, not evaluation
curves. Thresholds remained position MAE ≤5 px, max ≤18 px, scale MAE ≤0.025,
opacity MAE ≤0.035. Failure thresholds were not relaxed after observing results.
The correction uses full-resolution filtered comparison and final-rest pixel
calibration. The 122 reference frames and integrity baseline stayed unchanged.
Increasing native layout refresh passes did not change the extracted PNGs and
was not retained. The common Unity bridge was unchanged in the final fix.

These earlier development runs used the median patch estimator. Each opacity
MAE below covers all 61 native samples of that element.

| Existing UI | First recording | Second recording |
|---|---:|---:|
| Menu Panel | 0.00052 | 0.00056 |
| Navigation Information Panel | 0.00851 | 0.00089 |
| Logo | 0.00086 | 0.00089 |

Previously the first recording's navigation panel had MAE 0.05302. At 0.667 s
its native group opacity was 1.0 while the image fit reported 0.574. Its
opaque lettering retained the brightness of the rest frame. The patch estimator
recovered 1.0 there. The largest remaining single-frame opacity error on
this panel was 0.11021 at 0.233 s. The Survivors regression passed each of
its four tracks with opacity MAEs between 0.00402 and 0.00697.

For the first navigation recording, 4 of 61 individual frames exceeded 0.035.
The 95th percentile was 0.03910. Across the 13 frames where the reference
opacity changed by more than 0.001, MAE was 0.02918. The second navigation
recording had 0 exceeding frames and MAE 0.00418 across its 13 fade frames.
These breakdowns use the original native captures and the public scorer.
The low-contrast sampling correction produced identical exported opacities
when the opacity stage was replayed on the two development recordings and
the Survivors regression (610 target-frame values in total).
See [the model, research sources and reproduction commands](method-and-evidence.md).

### Direction-reversal recording with a per-frame opacity gate

We froze a 61-frame motion specification, fitter hashes and thresholds before
scoring a new recording on the same three menu groups. Each target moves
out, reverses its horizontal displacement, fades down and returns to rest.
The median patch estimator met the mean-error criterion, with six individual
navigation frames above 0.035. Those frames informed the corroborated-peak
change. The release was fitted from the same 61 hash-verified PNGs and scored
from a fresh Unity Play Mode replay with a maximum opacity error gate of
0.035 per target.

| UI | Opacity MAE | Fade-only MAE | Opacity P95 | Worst opacity error | Frames above 0.035 |
|---|---:|---:|---:|---:|---:|
| Menu Panel | 0.00298 | 0.00371 | 0.00801 | 0.01154 | 0/61 |
| Navigation Information Panel | 0.00196 | 0.00242 | 0.00590 | 0.00719 | 0/61 |
| Logo | 0.00362 | 0.00442 | 0.01051 | 0.01220 | 0/61 |

At 0.667 s the native navigation opacity was 0.362. Several partially covered
strokes gave patch values near 0.25. Two separated pixel neighborhoods still
measured about 0.362, while the previous median gave 0.272. The largest
remaining navigation error occurs at 0.233 s. All six previous exceedances
and their before/after errors are in the bundled study.

On the two earlier menu recordings, replaying the opacity stage with the
saved fitted geometry gave navigation MAEs of 0.00720 and 0.00045. The first
recording still has three frames above 0.035 and a maximum of 0.11021.
The textured Survivors controls kept their previously scored opacities.

### Compressed MP4 replacement

We encoded native reference frames from the same uGUI menu as H.264, CRF 18,
YUV 4:2:0, BT.709 at 960×540 and 30 fps. A Unity Play Mode run fitted the
61-frame reference. We then replaced its MP4 with a different 60-frame motion
at the same path and reran the same revision. The fitter produced 61 and 60
keys per target respectively. The native scorer sampled both replays through
the 2-second endpoint. The table gives maximum absolute opacity error.

| Target | Original 61-frame MP4 | Replacement 60-frame MP4 | Additional 61-frame menu motion |
|---|---:|---:|---:|
| Menu panel | 0.02396 | 0.01727 | 0.00662 |
| Navigation information panel | 0.02322 | 0.02240 | 0.10335 |
| Logo | 0.00658 | 0.00506 | 0.00769 |

The first two inputs informed the correction. On the replacement, the largest
navigation error fell from 0.29919 to 0.02240. In the additional motion the
navigation panel missed the 0.035 limit at 0.233, 0.267 and 0.433 seconds.
It had usable video measurements in 19 of the 25 frames before its rest
segment. All three runs passed checks on seven button raycast targets,
serialized listeners and references, layout at both viewport sizes, and the
271-file integrity baseline. These measurements cover this scene and encoding.

The navigation panel's mean position error was 1.65 px and its maximum was
11.27 px. Native replay passed seven button raycast probes, listener and
reference checks, pose restoration and both viewport checks. The asset audit
matched 271 files. The bundled study records the initial and release hashes,
both scores and the input frame hashes. [Study data](../benchmarks/evidence/study.json)

Native preservation checks compare layout, serialized references and listeners,
restoration, raycast destinations and two viewport sizes. Real-game callbacks
were not invoked. A separate fixture checked a known safe callback. This does
not establish game-specific purchases/navigation/authentication behavior.

Source repositories remained clean. Final native validation kept the original
post-import integrity baselines during bridge/template iterations; it did not
reset them. The old profile benchmark was not rerun, and its time/cost savings
must not be attributed to the new automatic discovery path.
