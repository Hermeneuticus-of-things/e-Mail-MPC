#!/usr/bin/env python3
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION_FILE = ROOT / 'version.json'
MANIFEST_FILE = ROOT / 'release-manifest.json'


def read_json(path: Path):
    with path.open('r', encoding='utf-8') as fh:
        return json.load(fh)


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as fh:
        for chunk in iter(lambda: fh.read(65536), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    version_data = read_json(VERSION_FILE)
    version = version_data.get('version')
    if not re.fullmatch(r'\d+\.\d+\.\d+', str(version)):
        raise SystemExit(f'Invalid version in version.json: {version!r}')
    expected_version = os.environ.get('EXPECTED_RELEASE_VERSION')
    if expected_version:
        expected_version = expected_version.removeprefix('v')
        if expected_version != version:
            raise SystemExit(f'Release version {expected_version} does not match version.json {version}')

    manifest = read_json(MANIFEST_FILE)
    if manifest.get('version') != version:
        raise SystemExit(f'Manifest version mismatch: {manifest.get("version")} != {version}')

    if manifest.get('installerUrl') != (
        'https://github.com/Hermeneuticus-of-things/e-Mail-MPC/releases/latest/download/'
        'eMail-MPC-windows-x64-setup.exe'
    ):
        raise SystemExit('Manifest installerUrl does not match the stable setup asset URL')
    if manifest.get('url') != (
        'https://github.com/Hermeneuticus-of-things/e-Mail-MPC/releases/latest/download/program.zip'
    ) or manifest.get('stableUrl') != manifest.get('url'):
        raise SystemExit('Manifest URL does not match the stable portable asset URL')

    portable_artifact = os.environ.get('RELEASE_PORTABLE_PATH') or os.environ.get('RELEASE_ARTIFACT_PATH')
    installer_artifact = os.environ.get('RELEASE_INSTALLER_PATH')
    if portable_artifact:
        path = Path(portable_artifact)
        if not path.exists():
            raise SystemExit(f'Artifact not found: {path}')
        manifest['sha256'] = sha256(path)
        manifest['sizeBytes'] = path.stat().st_size
    if installer_artifact:
        path = Path(installer_artifact)
        if not path.exists():
            raise SystemExit(f'Artifact not found: {path}')
        manifest['installerSha256'] = sha256(path)
        manifest['installerSizeBytes'] = path.stat().st_size
    if portable_artifact or installer_artifact:
        manifest['updatedAt'] = datetime.now(timezone.utc).replace(
            microsecond=0
        ).isoformat().replace('+00:00', 'Z')
        with MANIFEST_FILE.open('w', encoding='utf-8') as fh:
            json.dump(manifest, fh, indent=2)
            fh.write('\n')

    print(f'Validated release metadata for version {version}')


if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        raise SystemExit(1)
