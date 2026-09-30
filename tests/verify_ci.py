"""Check that CI builds and tests on each supported operating system."""

from pathlib import Path


workflow = Path(__file__).resolve().parents[1] / ".github/workflows/ci.yml"
text = workflow.read_text()

for runner in ("windows-latest", "macos-latest", "ubuntu-latest"):
    assert runner in text, runner

assert "dotnet build Tessitura.sln" in text
assert "dotnet test Tessitura.sln" in text
