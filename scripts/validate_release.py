#!/usr/bin/env python3
import hashlib
import json
import os
import re
import sys
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

    manifest = read_json(MANIFEST_FILE)
    if manifest.get('version') != version:
        raise SystemExit(f'Manifest version mismatch: {manifest.get("version")} != {version}')

    artifact = os.environ.get('RELEASE_ARTIFACT_PATH')
    if artifact:
        path = Path(artifact)
        if not path.exists():
            raise SystemExit(f'Artifact not found: {path}')
        manifest['sha256'] = sha256(path)
        manifest['sizeBytes'] = path.stat().st_size
        manifest['updatedAt'] = __import__('datetime').datetime.utcnow().replace(microsecond=0).isoformat() + 'Z'
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
