"""Bundle tracked PartyPilot sources with the built Windows preview artifacts."""

from pathlib import Path
from subprocess import check_output
from zipfile import ZIP_DEFLATED, ZipFile
import os


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT.parent / "PartyPilot-preview.zip"
TEMP = OUTPUT.with_suffix(".zip.tmp")
REQUIRED = {
    "release/desktop/PartyPilot.Desktop.exe",
    "release/bridge/PartyPilot.Bridge.exe",
    "release/PartyPilot-LiveFollow.exe",
    "release/PartyPilot-FpsConfig.exe",
    "release/PartyPilot.Sensor.dll",
    "release/PartyPilot.Sensor.deps.json",
}


def main() -> None:
    tracked = {
        ROOT / Path(name.decode())
        for name in check_output(["git", "ls-files", "-z"], cwd=ROOT).split(b"\0")
        if name
    }
    release = {
        path
        for path in (ROOT / "release").rglob("*")
        if path.is_file()
        and (path.suffix.lower() in {".exe", ".dll", ".pdb"}
             or path.name == "PartyPilot.Sensor.deps.json")
    }
    missing = [name for name in sorted(REQUIRED) if not (ROOT / name).is_file()]
    if missing:
        raise SystemExit("Missing release artifacts: " + ", ".join(missing))
    with ZipFile(TEMP, "w", compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(tracked | release):
            archive.write(path, arcname="PartyPilot/" + str(path.relative_to(ROOT)))
    with ZipFile(TEMP) as archive:
        if corrupt := archive.testzip():
            raise SystemExit("Corrupt archive member: " + corrupt)
        names = set(archive.namelist())
        if not {"PartyPilot/" + name for name in REQUIRED} <= names:
            raise SystemExit("Archive is missing a required release artifact")
        print(f"Packaged {len(names)} files into {OUTPUT}")
    os.replace(TEMP, OUTPUT)


if __name__ == "__main__":
    main()
