"""Build a single Windows installer from fresh PartyPilot source outputs."""

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[1]
PAYLOAD = (
    "desktop/PartyPilot.Desktop.exe",
    "bridge/PartyPilot.Bridge.exe",
    "probe/PartyPilot.Probe.exe",
    "PartyPilot-LiveFollow.exe",
    "PartyPilot-ClientLaunch.exe",
    "native/PartyPilot.PrivateSlots.dll",
    "native/PartyPilot.MultiInstance.dll",
    "sensor-plugin/PartyPilot.Sensor/PartyPilot.Sensor.dll",
    "sensor-plugin/PartyPilot.Sensor/PartyPilot.Sensor.deps.json",
)


def run(*args: str, env: dict[str, str] | None = None) -> None:
    subprocess.run(args, cwd=ROOT, env=env, check=True)


def validate_payload(root: Path) -> None:
    missing = [name for name in PAYLOAD if not (root / name).is_file()]
    if missing:
        raise SystemExit("Missing installer payload: " + ", ".join(missing))
    for name in PAYLOAD:
        path = root / name
        if path.suffix.lower() in {".exe", ".dll"}:
            with path.open("rb") as binary:
                if binary.read(2) != b"MZ":
                    raise SystemExit(f"Not a Windows PE file: {path}")
    dependencies = root / "sensor-plugin/PartyPilot.Sensor/PartyPilot.Sensor.deps.json"
    if not isinstance(json.loads(dependencies.read_text(encoding="utf-8")), dict):
        raise SystemExit(f"Invalid sensor dependency manifest: {dependencies}")


def find_compiler() -> Path:
    candidates = [shutil.which("ISCC.exe"), os.environ.get("ISCC_PATH")]
    for root in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles")):
        if root:
            candidates.append(str(Path(root) / "Inno Setup 6" / "ISCC.exe"))
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return Path(candidate)
    raise SystemExit("Inno Setup 6 compiler (ISCC.exe) was not found")


def copy_built(source: Path, destination: Path) -> None:
    if not source.is_file():
        raise SystemExit(f"Build output is missing: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def build(stage: Path, work: Path, exilecore_dir: Path) -> None:
    for project, folder in (
        ("ui/PartyPilot.Desktop.csproj", "desktop"),
        ("bridge/PartyPilot.Bridge.csproj", "bridge"),
        ("probe/PartyPilot.Probe.csproj", "probe"),
    ):
        run("dotnet", "publish", project, "-c", "Release", "-r", "win-x64",
            "--self-contained", "true", "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true", "-o", str(stage / folder))

    env = {**os.environ, "EXILECORE2_DIR": str(exilecore_dir)}
    run("dotnet", "build", "sensor/PartyPilot.Sensor.csproj", "-c", "Release", env=env)
    plugin = ROOT / "sensor/bin/Release/net8.0-windows"
    for name in ("PartyPilot.Sensor.dll", "PartyPilot.Sensor.deps.json"):
        copy_built(plugin / name, stage / "sensor-plugin/PartyPilot.Sensor" / name)

    go_env = {**os.environ, "GOOS": "windows", "GOARCH": "amd64"}
    for command, name in (
        ("livefollow", "PartyPilot-LiveFollow.exe"),
        ("clientlaunch", "PartyPilot-ClientLaunch.exe"),
    ):
        run("go", "build", "-o", str(stage / name), "./cmd/" + command,
            env=go_env)

    for source, name in (
        ("native/private-slot-runtime", "PartyPilot.PrivateSlots.dll"),
        ("native/multi-instance-runtime", "PartyPilot.MultiInstance.dll"),
    ):
        target = work / name.removesuffix(".dll")
        run("cmake", "-S", source, "-B", str(target),
            "-G", "Visual Studio 17 2022", "-A", "x64")
        run("cmake", "--build", str(target), "--config", "Release", "--parallel")
        run("ctest", "--test-dir", str(target), "-C", "Release", "--output-on-failure")
        copy_built(target / "bin/Release" / name, stage / "native" / name)

    validate_payload(stage)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-payload", type=Path,
                        help="check file layout only; does not verify build freshness")
    parser.add_argument("--exilecore2-dir", type=Path,
                        help="directory containing the ExileCore2.dll build reference")
    parser.add_argument("--version", help="installer build version")
    parser.add_argument("--output", type=Path, default=ROOT / "installer/out")
    options = parser.parse_args()
    if options.check_payload:
        validate_payload(options.check_payload.resolve())
        print("Installer payload structure is complete; build freshness is not checked.")
        return
    if os.name != "nt":
        parser.error("installer compilation requires Windows")
    if not options.version or not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.-]{0,40}",
                                               options.version):
        parser.error("--version must use letters, numbers, periods or hyphens")
    if not options.exilecore2_dir or not (options.exilecore2_dir / "ExileCore2.dll").is_file():
        parser.error("--exilecore2-dir must contain ExileCore2.dll")
    compiler = find_compiler()
    output = options.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="partypilot-installer-") as temporary:
        base = Path(temporary)
        stage = base / "payload"
        stage.mkdir()
        build(stage, base / "build", options.exilecore2_dir.resolve())
        run(str(compiler), f"-dStageDir={stage}",
            f"-dBuildVersion={options.version}", f"-o{output}",
            str(ROOT / "installer/PartyPilot.iss"))
    setup = output / f"PartyPilot-Setup-{options.version}.exe"
    if not setup.is_file():
        raise SystemExit(f"Installer was not produced: {setup}")
    with setup.open("rb") as binary:
        if binary.read(2) != b"MZ":
            raise SystemExit(f"Installer is not a Windows executable: {setup}")
    print(f"Installer built: {setup}")


if __name__ == "__main__":
    main()
