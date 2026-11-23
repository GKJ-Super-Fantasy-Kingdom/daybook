"""Create the Thunderstore ZIP from the current Release build and listing files."""
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from xml.etree import ElementTree
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(__file__).resolve().parent
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
project = ElementTree.parse(root / "Daybook.csproj")
version = manifest["version_number"]
if version != project.findtext(".//Version"):
    raise SystemExit("Match the project and manifest versions before packaging.")

framework = project.findtext(".//TargetFramework")
files = {
    "plugins/Daybook.dll": root / "bin" / "Release" / framework / "Daybook.dll",
    "manifest.json": root / "manifest.json",
    "README.md": root / "THUNDERSTORE.md",
    "CHANGELOG.md": root / "CHANGELOG.md",
    "icon.png": root / "icon.png",
}
output = root / "dist" / f"Daybook-{version}.zip"
output.parent.mkdir(exist_ok=True)
with TemporaryDirectory(dir=output.parent) as scratch:
    staged = Path(scratch) / output.name
    with ZipFile(staged, "w", ZIP_DEFLATED) as archive:
        for name, source in files.items():
            archive.write(source, name)
    with ZipFile(staged) as archive:
        if archive.testzip() is not None:
            raise SystemExit("ZIP verification failed.")
    staged.replace(output)
print(output)
