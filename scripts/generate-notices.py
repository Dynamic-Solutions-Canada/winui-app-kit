#!/usr/bin/env python3
"""The third-party notices generator every Windows app runs as `generate:notices` and `check:notices` (HYG-3, D-085).

Run from the app's repo root (npm does this):
    python3 external/winui-app-kit/scripts/generate-notices.py          write the file
    python3 external/winui-app-kit/scripts/generate-notices.py --check  fail if the committed file is out of date (validate)

It reads the optional `notices` block of the app's windows-build.json:
    "notices": {
      "project": "MyApp/MyApp.csproj",              default: the top-level "project"
      "output": "MyApp/THIRD-PARTY-NOTICES.txt",    default: THIRD-PARTY-NOTICES.txt beside the project
      "product_name": "My App",                     default: listing.product_name
      "about_source": "MyApp/Classes/AboutContent.cs"  optional: the file defining NoticesPath
    }

Reads the project's obj/project.assets.json, which any restore writes (`npm run format:check` restores
the project first in `validate`), and stops if that restore predates the package versions declared now
(after a kit pin bump, say), so it never writes or passes the old list. It also reads each package's
.nuspec and licence files from the NuGet package cache ($NUGET_PACKAGES, default ~/.nuget/packages). Every package in the graph is listed except
build-time tooling (names containing "BuildTools"), which never ships in the MSIX.

Licences are written out in full: a package's own licence file when it has one, otherwise the standard
text for its SPDX expression. An expression the script has no text for fails the run, so a new
dependency is never shipped without its licence; add the text to SPDX_TEXTS in the kit. Identical texts
are printed once and referenced. A package under a licence that requires it (Apache-2.0) has the
NOTICE file it ships passed on, printed once like the licence texts. PACKAGE_NOTES adds a line for
package contents the nuspec doesn't describe. The .NET runtime that ships inside the self-contained
package is listed with a link to its notices.

--check also checks the app's links to the file: a string in the project's C# code that links to the
app's own notices on GitHub (its origin remote; links into other repositories are skipped) (`/blob/<branch>/...` or `/raw/<branch>/...` ending in the output's file name) must
name the output path, so the About page's link cannot point at a file that is not there. When
about_source is set, that file must also define `NoticesPath = "<output path>"`, the constant an app
builds its About link from.

Standard library only.
"""
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

PACKAGES = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages"))

# Licence texts too long to keep inline, stored verbatim from their official source (Apache-2.0 from
# https://www.apache.org/licenses/LICENSE-2.0.txt). Only surrounding blank lines are trimmed.
LICENCES = Path(__file__).resolve().parent / "licences"

# Apache-2.0 section 4(d): a NOTICE file the package ships must be passed on with it.
NOTICE_LICENCES = {"Apache-2.0"}
NOTICE_NAMES = {"notice", "notice.txt", "notice.md"}

# Facts about a package's contents that its nuspec doesn't state, keyed by lower-case package id.
PACKAGE_NOTES = {
    "sqlitepclraw.lib.e_sqlite3": "Includes the SQLite engine, which is in the public domain: https://www.sqlite.org/copyright.html",
}

# Standard texts for the SPDX expressions in use. The copyright line comes from each package's nuspec.
SPDX_TEXTS = {
    "MIT": """Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.""",
    "BSD-3-Clause": """Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions
   and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of
   conditions and the following disclaimer in the documentation and/or other materials provided
   with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to
   endorse or promote products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF
THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.""",
}

SPDX_TEXTS["Apache-2.0"] = (LICENCES / "Apache-2.0.txt").read_text(encoding="utf-8").strip("\n")

# Shipped inside the self-contained package but not in the NuGet graph (runtime packs).
RUNTIME_ENTRIES = [
    (".NET runtime and Windows Desktop runtime", "MIT",
     "https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT"),
]

HEADER = """{product}: third-party notices

{product} includes the components below. Each is listed with its version, its licence and where
to find it. Generated by `npm run generate:notices`; do not edit by hand.
"""


def fail(message):
    """Print an error and stop with a failing exit code."""
    print(f"generate-notices: {message}", file=sys.stderr)
    sys.exit(1)


def load_settings(root):
    """
    The project, output path and product name for the app at `root`, from its windows-build.json.
    Returns (assets_path, output_path, project_dir, product_name, about_source), where about_source is
    None when not configured; stops the run when a required value can't be found.
    """
    config_path = root / "windows-build.json"
    if not config_path.is_file():
        fail(f"no windows-build.json in {root}; run from the app's repo root")
    with config_path.open(encoding="utf-8-sig") as f:
        config = json.load(f)

    settings = config.get("notices") or {}
    project = settings.get("project") or config.get("project")
    if not project:
        fail("windows-build.json has no notices.project or project")
    product = settings.get("product_name") or (config.get("listing") or {}).get("product_name")
    if not product:
        fail("windows-build.json has no notices.product_name or listing.product_name")

    project_dir = (root / project).parent
    output = root / settings["output"] if settings.get("output") else project_dir / "THIRD-PARTY-NOTICES.txt"
    about_source = root / settings["about_source"] if settings.get("about_source") else None
    return project_dir / "obj" / "project.assets.json", output, project_dir, product, about_source


def shipped_packages(root, assets_path):
    """The (name, version) of every package in the restore graph, without build-time tooling."""
    if not assets_path.is_file():
        fail(f"{assets_path.relative_to(root).as_posix()} is missing; restore the project first (npm run format:check does)")

    assets = json.loads(assets_path.read_text(encoding="utf-8"))
    stale_restore(root, assets_path, assets)
    found = set()
    for key, library in assets.get("libraries", {}).items():
        if library.get("type") != "package":
            continue
        name, version = key.split("/", 1)
        if "BuildTools" in name:
            continue
        found.add((name, version))
    return sorted(found, key=lambda p: p[0].lower())


def declared_versions(root):
    """
    The central package versions the next restore will use: the kit's build/WinUIAppKit.Packages.props,
    then the app's own Directory.Packages.props, a later definition winning. Returns {name: version}.
    """
    sources = [Path(__file__).resolve().parent.parent / "build" / "WinUIAppKit.Packages.props", root / "Directory.Packages.props"]
    versions = {}
    for source in sources:
        if not source.is_file():
            continue
        text = source.read_text(encoding="utf-8-sig")
        for match in re.finditer(r'<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"', text):
            versions[match.group(1)] = match.group(2)
    return versions


def stale_restore(root, assets_path, assets):
    """
    Stop the run when the restore graph predates the declared package versions, for example right after
    a kit pin bump that moves the Windows App SDK. Without this, generating would rewrite the old list
    and --check would pass against it. Compares each centrally managed package the restore recorded
    with the version declared now; a package only one side knows is left alone.
    """
    declared = declared_versions(root)
    stale = []
    for framework in (assets.get("project", {}).get("frameworks") or {}).values():
        for name, version in (framework.get("centralPackageVersions") or {}).items():
            wanted = declared.get(name)
            if wanted is not None and wanted != version:
                stale.append(f"{name} {version} (now {wanted})")

    if stale:
        relative = assets_path.relative_to(root).as_posix()
        fail(f"{relative} is from an earlier restore: {', '.join(sorted(set(stale)))}; restore the project again first (npm run format:check does)")


def read_nuspec(name, version):
    """Licence, copyright and project URL from the package's .nuspec in the cache."""
    folder = PACKAGES / name.lower() / version.lower()
    nuspecs = list(folder.glob("*.nuspec"))
    if not nuspecs:
        fail(f"{name} {version} is not in the package cache at {PACKAGES}; restore first")

    root = ET.parse(nuspecs[0]).getroot()
    namespace = re.match(r"\{.*\}", root.tag)
    ns = namespace.group(0) if namespace else ""
    metadata = root.find(f"{ns}metadata")

    def text(tag):
        node = metadata.find(f"{ns}{tag}")
        return (node.text or "").strip() if node is not None else ""

    license_node = metadata.find(f"{ns}license")
    license_type = license_node.get("type") if license_node is not None else ""
    license_value = (license_node.text or "").strip() if license_node is not None else ""
    return folder, license_type, license_value, text("copyright"), text("projectUrl")


def licence_text(name, folder, license_type, license_value):
    """The full licence text for a package: its own file, or the standard text of its expression."""
    if license_type == "file":
        path = folder / license_value
        if not path.is_file():
            fail(f"{name} names licence file {license_value}, which the package does not contain")
        return path.read_text(encoding="utf-8-sig").strip()

    if license_type == "expression":
        if license_value not in SPDX_TEXTS:
            fail(f"{name} uses licence {license_value!r}; add its standard text to SPDX_TEXTS in the kit before shipping it")
        return SPDX_TEXTS[license_value]

    fail(f"{name} declares no licence in its nuspec; check it by hand before shipping it")


def notice_text(folder, license_type, license_value):
    """
    The NOTICE file a package ships at its root, when its licence requires passing it on
    (NOTICE_LICENCES); otherwise None.
    """
    if license_type != "expression" or license_value not in NOTICE_LICENCES:
        return None
    for path in sorted(folder.iterdir()):
        if path.is_file() and path.name.lower() in NOTICE_NAMES:
            return path.read_text(encoding="utf-8-sig").strip()
    return None


def render(packages, product):
    """
    Build the notices text: one section per component, then each distinct licence text once, then
    each distinct NOTICE file once.
    """
    sections = []
    texts = []     # distinct licence texts, in first-use order
    notices = []   # distinct NOTICE files, in first-use order

    for name, version in packages:
        folder, license_type, license_value, copyright_line, project_url = read_nuspec(name, version)
        text = licence_text(name, folder, license_type, license_value)
        if text not in texts:
            texts.append(text)
        label = license_value if license_type == "expression" else "see licence text"
        lines = [f"{name} {version}", f"  Licence: {label} (text {texts.index(text) + 1} below)"]
        if copyright_line:
            lines.append(f"  {copyright_line}")
        if project_url:
            lines.append(f"  {project_url}")

        notice = notice_text(folder, license_type, license_value)
        if notice:
            if notice not in notices:
                notices.append(notice)
            lines.append(f"  NOTICE: notice {notices.index(notice) + 1} below")
        if name.lower() in PACKAGE_NOTES:
            lines.append(f"  Note: {PACKAGE_NOTES[name.lower()]}")
        sections.append("\n".join(lines))

    for component, licence, notices_url in RUNTIME_ENTRIES:
        sections.append(f"{component}\n  Licence: {licence}\n  Notices: {notices_url}")

    header = HEADER.format(product=product)
    body = [header, "=" * 100, "COMPONENTS", "=" * 100, "", "\n\n".join(sections), ""]
    for index, text in enumerate(texts, 1):
        body += ["=" * 100, f"LICENCE TEXT {index}", "=" * 100, "", text, ""]
    for index, text in enumerate(notices, 1):
        body += ["=" * 100, f"NOTICE {index}", "=" * 100, "", text, ""]
    return "\n".join(body).rstrip() + "\n"


def app_repository(root):
    """
    The app's GitHub "owner/repo" in lower case, from the origin remote of the repo at `root`, or None
    when there is no git, no origin, or the origin is not on GitHub.
    """
    try:
        result = subprocess.run(
            ["git", "-C", str(root), "remote", "get-url", "origin"],
            capture_output=True, text=True, check=False,
        )
    except OSError:
        return None
    match = re.search(r"github\.com[:/]([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", result.stdout.strip())
    return f"{match.group(1)}/{match.group(2)}".lower() if match else None


def wrong_links(root, project_dir, output):
    """
    Links in the project's C# code that point at the app's own notices on GitHub but name another
    path, as "file:line: message" strings. A link is a string literal containing /blob/<ref>/ or
    /raw/<ref>/ and ending in the output's file name (any case); it must end with the output's
    repo-relative path.

    Links into another repository (dotnet/runtime's notices, say) are left alone. A literal with no
    github.com/<owner>/<repo> in it is a fragment appended to the app's own source URL, so it counts
    as the app's. When the app's repository can't be determined, every matching link is checked.
    """
    expected = output.relative_to(root).as_posix()
    own_repository = app_repository(root)
    link = re.compile(r'"([^"\n]*?)/(?:blob|raw)/[^/"\n]+/([^"\n]*)"')
    repository_in_url = re.compile(r"github\.com/([^/]+)/([^/]+)$")
    problems = []

    for source in sorted(project_dir.rglob("*.cs")):
        parts = source.relative_to(project_dir).parts
        if parts and parts[0] in ("bin", "obj"):
            continue
        lines = source.read_text(encoding="utf-8-sig").splitlines()
        for number, line in enumerate(lines, 1):
            for match in link.finditer(line):
                prefix, path = match.group(1), match.group(2)
                if not path.lower().endswith(output.name.lower()) or path == expected:
                    continue

                # Another repository's notices are not this app's file.
                linked = repository_in_url.search(prefix)
                if linked and own_repository and f"{linked.group(1)}/{linked.group(2)}".lower() != own_repository:
                    continue

                where = source.relative_to(root).as_posix()
                problems.append(f"{where}:{number}: links to {path}, but the notices are at {expected}")
    return problems


def wrong_notices_path(root, about_source, output):
    """
    Problems with the NoticesPath constant in `about_source`: missing, or not the output's
    repo-relative path. Returns a list of messages, empty when the constant is right.
    """
    where = about_source.relative_to(root).as_posix()
    if not about_source.is_file():
        return [f"{where} (notices.about_source) does not exist"]

    expected = output.relative_to(root).as_posix()
    match = re.search(r'NoticesPath\s*=\s*"([^"]*)"', about_source.read_text(encoding="utf-8-sig"))
    if not match:
        return [f"{where} has no NoticesPath constant for the About page's notices link"]
    if match.group(1) != expected:
        return [f"{where}: NoticesPath is {match.group(1)!r}, but the notices are at {expected!r}"]
    return []


def main():
    """Write the notices, or with --check compare them with the committed file and check links to it."""
    root = Path.cwd()
    assets_path, output, project_dir, product, about_source = load_settings(root)
    content = render(shipped_packages(root, assets_path), product)
    relative = output.relative_to(root).as_posix()

    if "--check" in sys.argv[1:]:
        problems = []
        current = output.read_text(encoding="utf-8") if output.is_file() else ""
        if current != content:
            problems.append(f"{relative} is out of date; run `npm run generate:notices` and commit it")
        problems += wrong_links(root, project_dir, output)
        if about_source is not None:
            problems += wrong_notices_path(root, about_source, output)

        for problem in problems:
            print("error:", problem, file=sys.stderr)
        if problems:
            print(f"check:notices: failed ({len(problems)} problem(s))")
            sys.exit(1)
        print("check:notices: passed")
        return

    if not output.parent.is_dir():
        fail(f"the folder for {relative} does not exist; check notices.output in windows-build.json")
    output.write_text(content, encoding="utf-8")
    print(f"generate-notices: wrote {relative}")


if __name__ == "__main__":
    main()
