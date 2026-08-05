#!/usr/bin/env python3
"""Verify all release and PWA version projections against version.props."""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent
VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$")


class VerificationError(RuntimeError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise VerificationError(message)


def read(relative_path: str) -> str:
    path = ROOT / relative_path
    require(path.is_file(), f"Required version consumer is missing: {relative_path}")
    return path.read_text(encoding="utf-8-sig")


def load_metadata() -> dict[str, Any]:
    path = ROOT / "version.props"
    require(path.is_file(), "Canonical version metadata is missing: version.props")
    properties = ET.parse(path).getroot().find("PropertyGroup")
    require(properties is not None, "version.props must contain a PropertyGroup")

    def value(name: str) -> str:
        element = properties.find(name)
        require(element is not None and bool(element.text), f"version.props is missing {name}")
        return element.text.strip()

    metadata: dict[str, Any] = {
        "version": value("PlayerAssistantVersion"),
        "assemblyVersion": value("PlayerAssistantAssemblyVersion"),
        "pwaVersion": value("PlayerAssistantPwaVersion"),
    }
    for key, property_name in {
        "metadataRevision": "PlayerAssistantPwaMetadataRevision",
        "stylesRevision": "PlayerAssistantPwaStylesRevision",
        "appRevision": "PlayerAssistantPwaAppRevision",
        "cacheRevision": "PlayerAssistantPwaCacheRevision",
    }.items():
        raw = value(property_name)
        require(raw.isdigit() and int(raw) > 0, f"{property_name} must be a positive integer")
        metadata[key] = int(raw)

    require(bool(VERSION_PATTERN.fullmatch(metadata["version"])), "Desktop version is invalid")
    require(bool(VERSION_PATTERN.fullmatch(metadata["pwaVersion"])), "PWA version is invalid")
    metadata["installerVersion"] = re.split(r"[-+]", metadata["version"], maxsplit=1)[0]
    require(metadata["assemblyVersion"] == f"{metadata['installerVersion']}.0",
            "Assembly version must equal the numeric desktop version plus .0")
    return metadata


def verify_desktop(metadata: dict[str, Any]) -> None:
    project = read("player-assistant.csproj")
    require('<Import Project="version.props" />' in project, "Desktop project must import version.props")
    for element, expected in {
        "Version": "$(PlayerAssistantVersion)",
        "AssemblyVersion": "$(PlayerAssistantAssemblyVersion)",
        "FileVersion": "$(PlayerAssistantAssemblyVersion)",
        "InformationalVersion": "$(PlayerAssistantVersion)",
    }.items():
        require(f"<{element}>{expected}</{element}>" in project, f"Desktop {element} must derive from canonical version metadata")
    require(metadata["version"] not in project, "Desktop project must not duplicate the canonical version literal")


def verify_release_scripts(metadata: dict[str, Any]) -> None:
    helper = read("version-metadata.ps1")
    for property_name in (
        "PlayerAssistantVersion",
        "PlayerAssistantAssemblyVersion",
        "PlayerAssistantPwaVersion",
        "PlayerAssistantPwaMetadataRevision",
        "PlayerAssistantPwaStylesRevision",
        "PlayerAssistantPwaAppRevision",
        "PlayerAssistantPwaCacheRevision",
    ):
        require(property_name in helper, f"PowerShell version helper does not expose {property_name}")

    consumers = (
        "build-installer.ps1",
        "build-release-update-artifacts.ps1",
        "publish-player-assistant.ps1",
        "verify-installer-package.ps1",
        "verify-release-update-artifacts.ps1",
        "verify-installer-clean-machine-smoke.ps1",
        "verify-rc-checklist.ps1",
    )
    for relative_path in consumers:
        content = read(relative_path)
        require("version-metadata.ps1" in content and "Get-PlayerAssistantVersionMetadata" in content,
                f"{relative_path} must load canonical version metadata")
        require(metadata["version"] not in content, f"{relative_path} duplicates the canonical desktop version")

    installer = read("Installer/install-player-assistant.ps1")
    require("FileVersionInfo]::GetVersionInfo($payloadExecutablePath).ProductVersion" in installer,
            "Packaged installer must derive its version from the payload executable")
    require(metadata["version"] not in installer, "Packaged installer duplicates the canonical desktop version")

    inno = read("Installer/player-assistant.iss")
    require("#error Version must be supplied from version.props by build-installer.ps1" in inno,
            "Inno Setup must require its version from build-installer.ps1")
    require(metadata["version"] not in inno, "Inno Setup duplicates the canonical desktop version")

    workflow = read(".github/workflows/hardening.yml")
    require("Load canonical version metadata" in workflow and "Get-PlayerAssistantVersionMetadata" in workflow,
            "Hardening workflow must load canonical version metadata")
    require("INSTALLER_PACKAGE_PATH" in workflow and "UPDATE_ARCHIVE_PATH" in workflow and "UPDATE_INSTALLER_PATH" in workflow,
            "Hardening workflow artifact paths must derive from canonical metadata")
    require(metadata["version"] not in workflow, "Hardening workflow duplicates the canonical desktop version")


def expected_pwa_version_script(metadata: dict[str, Any]) -> str:
    return """'use strict';

globalThis.PLAYER_ASSISTANT_VERSION_METADATA = Object.freeze({
    pwaVersion: '%s',
    metadataRevision: %d,
    stylesRevision: %d,
    appRevision: %d,
    cacheRevision: %d
});
""" % (
        metadata["pwaVersion"],
        metadata["metadataRevision"],
        metadata["stylesRevision"],
        metadata["appRevision"],
        metadata["cacheRevision"],
    )


def write_pwa_projections(metadata: dict[str, Any]) -> None:
    (ROOT / "pwa/version.js").write_text(expected_pwa_version_script(metadata), encoding="utf-8", newline="\r\n")

    index_path = ROOT / "pwa/index.html"
    html = index_path.read_text(encoding="utf-8-sig")
    substitutions = (
        (r"styles[.]css[?]v=\d+", f"styles.css?v={metadata['stylesRevision']}"),
        (r"version[.]js[?]v=\d+", f"version.js?v={metadata['metadataRevision']}"),
        (r"app[.]js[?]v=\d+", f"app.js?v={metadata['appRevision']}"),
        (r"<strong>[^<]+ PWA</strong>", f"<strong>{metadata['pwaVersion']} PWA</strong>"),
    )
    for pattern, replacement in substitutions:
        html, count = re.subn(pattern, replacement, html, count=1)
        require(count == 1, f"Unable to update PWA projection matching {pattern}")
    index_path.write_text(html, encoding="utf-8", newline="\r\n")

    worker_path = ROOT / "pwa/service-worker.js"
    worker = worker_path.read_text(encoding="utf-8-sig")
    worker, count = re.subn(
        r"importScripts[(]'[.]\/version[.]js[?]v=\d+'[)]",
        f"importScripts('./version.js?v={metadata['metadataRevision']}')",
        worker,
        count=1,
    )
    require(count == 1, "Unable to update the service-worker metadata revision")
    worker_path.write_text(worker, encoding="utf-8", newline="\r\n")


def verify_pwa(metadata: dict[str, Any]) -> None:
    version_script = read("pwa/version.js")
    expected_script = expected_pwa_version_script(metadata)
    require(version_script.replace("\r\n", "\n") == expected_script,
            "pwa/version.js has drifted from version.props")

    html = read("pwa/index.html")
    require(f'href="styles.css?v={metadata["stylesRevision"]}"' in html, "PWA stylesheet revision has drifted")
    require(f'src="version.js?v={metadata["metadataRevision"]}"' in html, "PWA metadata revision has drifted")
    require(f'src="app.js?v={metadata["appRevision"]}"' in html, "PWA app revision has drifted")
    require(f'<strong>{metadata["pwaVersion"]} PWA</strong>' in html, "PWA visible version has drifted")

    app = read("pwa/app.js")
    require("PLAYER_ASSISTANT_VERSION_METADATA?.pwaVersion" in app, "PWA app must consume canonical version metadata")
    require(f"const APP_VERSION = '{metadata['pwaVersion']}'" not in app, "PWA app duplicates its version literal")

    worker = read("pwa/service-worker.js")
    require(f"importScripts('./version.js?v={metadata['metadataRevision']}')" in worker,
            "Service worker metadata revision has drifted")
    for property_name in ("pwaVersion", "cacheRevision", "stylesRevision", "appRevision"):
        require(f"VERSION_METADATA.{property_name}" in worker, f"Service worker must consume {property_name}")
    require(f"player-assistant-pwa-{metadata['pwaVersion']}-v{metadata['cacheRevision']}" not in worker,
            "Service worker duplicates the resolved cache version")

    deployment = read("pwa/test-deployment.ps1")
    require("'version.js' = @('application/javascript', 'text/javascript')" in deployment,
            "PWA deployment verification must allow version.js")
    documentation = read("web-deploy/CHARACTER-AUTH-DEPLOY.md")
    require("version.js" in documentation, "PWA deployment documentation must include version.js")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Regenerate checked-in PWA version projections")
    arguments = parser.parse_args()
    try:
        metadata = load_metadata()
        if arguments.write:
            write_pwa_projections(metadata)
        verify_desktop(metadata)
        verify_release_scripts(metadata)
        verify_pwa(metadata)
    except (OSError, ET.ParseError, VerificationError, ValueError) as error:
        print(f"Version verification failed: {error}", file=sys.stderr)
        return 1

    print(
        "Canonical version metadata verified: "
        f"desktop={metadata['version']}, pwa={metadata['pwaVersion']}, "
        f"app-r{metadata['appRevision']}, cache-r{metadata['cacheRevision']}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
