# How the fitter uses image evidence

## Recovering opacity

Porter and Duff describe foreground-over-background compositing and premultiplied color. For one isolated layer over an opaque background, write the observed RGB as `I = B + a · c · (P − B)`. Here `P` is the flat foreground paint, `B` the background, `c` the pixel coverage of the graphic at its rest opacity and `a` its relative group opacity. The extra factorization of alpha into `a · c` is our model for a fading UI element. Multiple overlapping children can have a more complicated response to a group fade. [Porter & Duff, *Compositing Digital Images*, SIGGRAPH 1984, §4](https://graphics.pixar.com/library/Compositing/paper.pdf)

Levin, Lischinski and Weiss explain why unknown foreground color, background color and alpha cannot generally be recovered from one RGB measurement. Our capture supplies a background and an isolated RGBA template. For eligible flat paint, projecting `I − B` onto `P − B` gives an estimate of `a · c`. A small foreground–background difference amplifies measurement noise. The implementation filters low-contrast pixels and compares opaque interior samples with the final rest frame. [Levin, Lischinski & Weiss, *A Closed-Form Solution to Natural Image Matting*, IEEE TPAMI 2008, §1](https://people.csail.mit.edu/alevin/papers/Matting-Levin-Lischinski-Weiss-PAMI.pdf)

Our estimator uses 24-pixel tiles, checks overlap with other fitted elements, and takes the median of the usable patch estimates. Too few patches or excessive disagreement retains the image-fit estimate for that frame. Tile size, agreement gates and the 0.035 acceptance threshold are engineering choices from this project. The paper describes a separate solver for natural images.

At a subpixel pose, thin strokes in many patches can lose coverage together. An opaque interior that survives rasterization still measures the group opacity. Patches with a near-opaque rest peak (0.98 to 1.02) can supply a second measurement. We check that the color fits the foreground/background blend, that other bright samples support the peak, and that a second patch agrees within 0.01. The two observed peak locations must have disjoint five-pixel-wide neighborhoods, with at least two agreeing pixels in one neighborhood. If the lower of the two levels exceeds the median by more than 0.01, we use that level. The per-track fit diagnostics record the zero-based frame, median and replacement in `flatPaintOpacity.corroboratedPeaks`. These gates are project heuristics.

A synthetic control moves a flat graphic onto a background of exactly the same color. At true group opacity 0.47, every pixel there equals the background. An earlier version treated five such patches as measurements and replaced a fallback estimate of 0.31 with zero. The updated sampler excludes pixels where the squared RGB contrast is at most 0.01. In this control it retains 0.31 and records a fallback frame. Moving the same graphic onto a contrasting background recovers 0.47, and a fully transparent graphic there gives a measured zero. Run `python benchmarks/test_flat_opacity.py` to check both cases.

## Recovering translation and scale

Baker and Matthews formulate alignment as fitting a warped template to image measurements. Here the template is an existing Unity UI group. The fitted transform has two translation parameters and one uniform scale about its Unity pivot. Our implementation searches from the adjacent frame, refines candidates with Powell's method and estimates opacity for each candidate by least squares. Repeated text can produce competing alignments. The final-frame calibration assumes the recording ends at the original pose. [Baker & Matthews, *Lucas–Kanade 20 Years On: A Unifying Framework*, IJCV 2004, §2](https://www.cs.cmu.edu/afs/cs/academic/class/15869-f11/www/readings/Baker%26Matthews.pdf.1)

## Reading the evaluation

Baker et al. report displacement error, percentiles, fractions exceeding a threshold and results on regions with different tracking difficulty. We adapt that evaluation principle to **bound UI elements**. Position error is the Euclidean distance between corresponding group translations in pixels. Opacity error is the absolute difference between the two native relative group opacities. Unity computes the reference and replay samples. [Baker et al., *A Database and Evaluation Methodology for Optical Flow*, IJCV 2011, §4](https://vision.middlebury.edu/flow/floweval-ijcv2011.pdf)

The scorer reports the mean, nearest-rank 95th percentile, maximum and count of frames with opacity error above 0.035. It also reports the mean over frames where the reference opacity differs from the preceding frame by more than 0.001. The first frame has no predecessor. This exposes how much a long rest segment can lower the full-clip average. The default score uses each element's 61-frame opacity MAE ≤0.035 and the established visible-sample geometry limits. The reversal benchmark adds `--require-peak`, which also requires every frame of every element to have opacity error ≤0.035. Fade-only MAE and percentiles are diagnostic. Consecutive frames from one animation are correlated.

The original two menu recordings informed development. The first reversal run used a frozen fitter on a new movement in the same menu. Its six high-error navigation frames subsequently informed the peak-measurement change. The latest score is a regression against those exact reference frames. Replay numbers and button/layout checks are local project measurements. [Measured results](validation.md)

## Reproduce the reversal

Use the Windows / Unity 6000.3.21f1 setup and Python dependencies in the skill guide. From the skill directory, prepare the pinned project's menu at the validation resolution. [Setup](../SKILL.md) · [Source project at the tested revision](https://github.com/Anthelmed/UGUI-vs-UIToolkit/tree/01e0cfef4d8243e5561047ada161482582585043)

```powershell
git clone https://github.com/Anthelmed/UGUI-vs-UIToolkit.git C:/motion-demo/source
git -C C:/motion-demo/source checkout --detach 01e0cfef4d8243e5561047ada161482582585043
python scripts/unity.py prepare --source C:/motion-demo/source --workspace C:/motion-demo/work --scene "Assets/Samples/Main Menu/Main Menu Scene.unity" --width 960 --height 540
python benchmarks/reversal.py --workspace C:/motion-demo/work
```

Each run creates unique reference and replay revisions. Under the printed workspace benchmark folder, `protocol.json` records the motion, criteria and implementation hashes before rendering. `frame-hashes.json` identifies the 61 reference PNGs supplied to `match`. `score.json` contains per-element errors and native structural checks. `integrity.json` contains the asset audit. The benchmark returns exit code 2 on a completed run that misses a gate. To rescore an existing pair, run `python benchmarks/score.py TRUTH.json REPLAY.json --require-peak --out score.json`.
