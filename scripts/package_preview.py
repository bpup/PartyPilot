"""Bundle tracked PartyPilot sources with the built Windows preview artifacts."""

from pathlib import Path
from subprocess import check_output, run
from zipfile import ZIP_DEFLATED, ZipFile
import argparse
import os
import shutil


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT.parent / "PartyPilot-preview.zip"
TEMP = OUTPUT.with_suffix(".zip.tmp")
REQUIRED = {
    "release/desktop/PartyPilot.Desktop.exe",
    "release/bridge/PartyPilot.Bridge.exe",
    "release/PartyPilot-LiveFollow.exe",
    "release/PartyPilot-BridgeFeed.exe",
    "release/PartyPilot-PartyReplay.exe",
    "release/PartyPilot.exe",
    "release/PartyPilot-FpsConfig.exe",
    "release/PartyPilot-ClientLaunch.exe",
    "release/PartyPilot.Sensor.dll",
    "release/PartyPilot.Sensor.deps.json",
    "release/PartyPilot-SensorLog.exe",
    "release/PartyPilot-SlotAudit.exe",
    "release/PartyPilot-OrderedMotionLog.exe",
    "release/PartyPilot-CombatReplay.exe",
    "release/skillbind/PartyPilot.SkillBind.exe",
    "release/native/PartyPilot.PrivateSlots.dll",
    "release/native/PartyPilot.PrivateSlots.Smoke.exe",
    "release/native/PartyPilot.MultiInstance.dll",
    "release/native/PartyPilot.MultiInstance.Smoke.exe",
}

FOLLOW_BINARIES = {
    "release/PartyPilot-BridgeFeed.exe": "bridgefeed",
    "release/PartyPilot-LiveFollow.exe": "livefollow",
    "release/PartyPilot-PartyReplay.exe": "partyreplay",
    "release/PartyPilot.exe": "partypilot",
}


def refresh_binaries() -> None:
    exilecore = os.environ.get("EXILECORE2_DIR")
    if not exilecore or not (Path(exilecore) / "ExileCore2.dll").is_file():
        raise SystemExit("--refresh requires EXILECORE2_DIR with ExileCore2.dll")
    run(["dotnet", "publish", "ui/PartyPilot.Desktop.csproj", "-c", "Release",
         "-r", "win-x64", "--self-contained", "true",
         "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
         "-o", "release/desktop"], cwd=ROOT, check=True)
    run(["dotnet", "publish", "bridge/PartyPilot.Bridge.csproj", "-c", "Release",
         "-r", "win-x64", "--self-contained", "true",
         "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
         "-o", "release/bridge"], cwd=ROOT, check=True)
    run(["dotnet", "build", "sensor/PartyPilot.Sensor.csproj", "-c", "Release"],
        cwd=ROOT, env={**os.environ, "EXILECORE2_DIR": exilecore}, check=True)
    built = ROOT / "sensor/bin/Release/net8.0-windows"
    for name in ("PartyPilot.Sensor.dll", "PartyPilot.Sensor.deps.json"):
        shutil.copy2(built / name, ROOT / "release" / name)
    for output, command in FOLLOW_BINARIES.items():
        run(["go", "build", "-o", output, "./cmd/" + command], cwd=ROOT,
            env={**os.environ, "GOOS": "windows", "GOARCH": "amd64"}, check=True)


def check_binaries_current() -> None:
    def source_files(folder: str) -> list[Path]:
        return [path for path in (ROOT / folder).rglob("*.cs")
                if "obj" not in path.parts and "bin" not in path.parts]

    shared = [*ROOT.glob("data/*.json"), ROOT / "sensor/QuestTileRules.cs",
              ROOT / "sensor/GeneratedRoomClassifier.cs"]
    binaries = {
        ROOT / "release/desktop/PartyPilot.Desktop.exe":
            [*source_files("ui"), *ROOT.glob("common/*.cs"), *shared,
             ROOT / "ui/PartyPilot.Desktop.csproj"],
        ROOT / "release/bridge/PartyPilot.Bridge.exe":
            [*source_files("bridge"), *ROOT.glob("common/*.cs"),
             ROOT / "bridge/PartyPilot.Bridge.csproj"],
        ROOT / "release/PartyPilot.Sensor.dll":
            [*source_files("sensor"), *shared,
             ROOT / "sensor/PartyPilot.Sensor.csproj"]
    }
    follow_sources = [*ROOT.glob("internal/**/*.go"), ROOT / "go.mod"]
    for output, command in FOLLOW_BINARIES.items():
        binaries[ROOT / output] = [*follow_sources, *ROOT.glob(f"cmd/{command}/*.go")]
    stale = [str(path.relative_to(ROOT)) for path, sources in binaries.items()
             if not path.is_file() or not sources or
             path.stat().st_mtime_ns < max(source.stat().st_mtime_ns for source in sources)]
    if stale:
        raise SystemExit("Preview binaries are older than source: " + ", ".join(stale)
                         + "; rebuild with --refresh")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh", action="store_true",
                        help="rebuild desktop, sensor, and follow binaries before packaging")
    args = parser.parse_args()
    if args.refresh:
        refresh_binaries()
    check_binaries_current()
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
