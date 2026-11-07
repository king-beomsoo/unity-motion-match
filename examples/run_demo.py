"""Rebuild the Signal Run comparison in an empty Unity workspace."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workspace", type=Path, help="an empty folder outside the sample project")
    parser.add_argument("--editor", type=Path, help="path to the Unity editor executable")
    args = parser.parse_args()
    work = args.workspace.resolve()
    examples = Path(__file__).resolve().parent
    cli = [sys.executable, str(examples.parent / "scripts/unity.py")]
    prepare = cli + ["prepare", "--source", str(examples / "signal-run"), "--workspace", str(work),
                     "--scene", "Assets/Debrief.unity", "--target", "Debrief/Result",
                     "--target", "Debrief/Continue", "--width", "640", "--height", "360"]
    editor = args.editor
    if editor is None:
        version_file = examples / "signal-run/ProjectSettings/ProjectVersion.txt"
        version = version_file.read_text().splitlines()[0].split(": ", 1)[1]
        hub = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor"
        editor = hub / version / "Editor/Unity.exe"
        if not editor.is_file():
            parser.error(f"Install Unity {version} or use --editor PATH to select its executable.")
    prepare.extend(["--editor", str(editor)])
    subprocess.run(prepare, check=True)
    case, = json.loads((work / "workspace.json").read_text(encoding="utf-8-sig"))["cases"]
    references = work / "demo-references"
    recording = references / (case + "-r1") / "reference.mp4"
    recording.parent.mkdir(parents=True)
    shutil.copyfile(examples / "signal-run-reference.mp4", recording)
    subprocess.run(cli + ["match", "--workspace", str(work), "--reference", str(references),
                          "--revision", "r1"], check=True)
    print("Clips:", work / "unity/Assets/MotionMatchGenerated")
    print("Unity replay and preservation checks:", work / "evidence/r1")


if __name__ == "__main__":
    main()
