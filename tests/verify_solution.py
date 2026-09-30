"""Check the F0.1 solution scaffold."""

from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
PROJECTS = (
    "src/Tessitura.Core",
    "src/Tessitura.Smufl",
    "src/Tessitura.Engraving",
    "src/Tessitura.Rendering",
    "src/Tessitura.Editing",
    "src/Tessitura.Playback",
    "src/Tessitura.IO",
    "src/Tessitura.App",
    "src/Tessitura.Desktop",
    "tests/Tessitura.Core.Tests",
    "tests/Tessitura.Engraving.Tests",
    "tests/Tessitura.IO.Tests",
    "tests/Tessitura.Benchmarks",
)


def main() -> None:
    solution = (ROOT / "Tessitura.sln").read_text()
    for directory in PROJECTS:
        name = directory.rsplit("/", 1)[-1]
        project = ROOT / directory / f"{name}.csproj"
        assert project.is_file(), project
        assert name in solution, name

    build = ET.parse(ROOT / "Directory.Build.props").getroot()
    values = {child.tag: child.text for child in build.iter()}
    assert values["TargetFramework"] == "net10.0"
    assert values["Nullable"] == "enable"
    assert values["TreatWarningsAsErrors"] == "true"

    packages = ET.parse(ROOT / "Directory.Packages.props").getroot()
    values = {child.tag: child.text for child in packages.iter()}
    assert values["ManagePackageVersionsCentrally"] == "true"


if __name__ == "__main__":
    main()
