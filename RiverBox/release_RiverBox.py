#!/usr/bin/env python3
import urllib.request
import json
import ssl
import os
import sys
import zipfile
import xml.etree.ElementTree as ET

# === 配置 ===
REPO = 'Whswa-river/River'
COLLECTION_REPO = 'Whswa-river/PluginRepo'
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CSPROJ_PATH = os.path.join(SCRIPT_DIR, 'RiverBox.csproj')
ZIP_PATH = os.path.join(SCRIPT_DIR, 'bin', 'Release', 'release_RiverBox.zip')
COLLECTION_JSON_PATH = os.path.normpath(os.path.join(SCRIPT_DIR, '..', '..', 'PluginRepo', 'plugins.json'))
MANIFEST_JSON_PATH = os.path.normpath(os.path.join(SCRIPT_DIR, '..', 'RiverBox.json'))

# Token 从环境变量读取，禁止写死在代码里
RIVER_TOKEN = os.environ.get('RIVER_TOKEN') or os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')


def get_version():
    tree = ET.parse(CSPROJ_PATH)
    root = tree.getroot()
    ns = '{http://schemas.microsoft.com/developer/msbuild/2003}'
    for elem in root.iter(f'{ns}Version'):
        return elem.text
    for elem in root.iter('Version'):
        return elem.text
    raise Exception('Version not found in csproj')


def build():
    print('1. Building...')
    ret = os.system(f'dotnet build -c Release "{CSPROJ_PATH}"')
    if ret != 0:
        print('Build failed!')
        sys.exit(1)


def package(version):
    print('2. Packaging...')
    os.makedirs(os.path.dirname(ZIP_PATH), exist_ok=True)
    build_dir = os.path.join(SCRIPT_DIR, 'bin', 'Release')

    files_to_include = [
        'RiverBox.dll',
        'ECommons.dll',
        'RiverBox.deps.json',
        'RiverBox.json',
        'icon.png',
    ]

    missing = []
    with zipfile.ZipFile(ZIP_PATH, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for file in files_to_include:
            file_path = os.path.join(build_dir, file)
            if not os.path.exists(file_path):
                file_path = os.path.join(build_dir, 'images', file)
            if os.path.exists(file_path):
                zipf.write(file_path, file)
            else:
                missing.append(file)
                print(f'   Warning: {file} not found')

    critical = {'RiverBox.dll', 'ECommons.dll', 'RiverBox.json'}
    if critical & set(missing):
        print(f'   Error: missing critical files: {sorted(critical & set(missing))}')
        sys.exit(1)

    print(f'   Created {ZIP_PATH}')


def create_release(token, version):
    print(f'3. Creating release {version}...')
    ctx = ssl.create_default_context()

    data = json.dumps({
        'tag_name': f'RiverBox-{version}',
        'name': f'RiverBox {version}',
        'body': f'Release {version}',
        'draft': False,
        'prerelease': False
    }).encode('utf-8')

    req = urllib.request.Request(
        f'https://api.github.com/repos/{REPO}/releases',
        data=data,
        headers={
            'Authorization': f'token {token}',
            'Accept': 'application/vnd.github+json',
            'Content-Type': 'application/json'
        },
        method='POST'
    )

    with urllib.request.urlopen(req, context=ctx) as resp:
        result = json.loads(resp.read())
        release_id = result['id']
        print(f'   Release created (ID: {release_id})')

    with open(ZIP_PATH, 'rb') as f:
        zip_data = f.read()

    req2 = urllib.request.Request(
        f'https://uploads.github.com/repos/{REPO}/releases/{release_id}/assets?name=RiverBox.zip',
        data=zip_data,
        headers={
            'Authorization': f'token {token}',
            'Content-Type': 'application/zip',
            'Accept': 'application/vnd.github+json'
        },
        method='POST'
    )

    with urllib.request.urlopen(req2, context=ctx) as resp2:
        result2 = json.loads(resp2.read())
        download_url = result2['browser_download_url']
        print(f'   Uploaded: {download_url}')
        return download_url


def update_manifest_json(version, download_url):
    print('4. Updating RiverBox.json...')
    if not os.path.exists(MANIFEST_JSON_PATH):
        print(f'   Manifest not found at {MANIFEST_JSON_PATH}')
        return

    with open(MANIFEST_JSON_PATH, 'r', encoding='utf-8') as f:
        data = json.load(f)

    for item in data:
        if item.get('InternalName') == 'RiverBox':
            item['AssemblyVersion'] = version
            item['DownloadLinkInstall'] = download_url
            item['DownloadLinkTesting'] = download_url
            item['DownloadLinkUpdate'] = download_url
            break

    with open(MANIFEST_JSON_PATH, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        f.write('\n')
    print('   RiverBox.json updated')


def update_collection_json(version, download_url):
    print('5. Updating collection JSON...')
    if not os.path.exists(COLLECTION_JSON_PATH):
        print(f'   Collection JSON not found at {COLLECTION_JSON_PATH}')
        return

    with open(COLLECTION_JSON_PATH, 'r', encoding='utf-8') as f:
        data = json.load(f)

    for item in data:
        if item.get('InternalName') == 'RiverBox':
            item['AssemblyVersion'] = version
            item['DownloadLinkInstall'] = download_url
            item['DownloadLinkTesting'] = download_url
            item['DownloadLinkUpdate'] = download_url
            break

    with open(COLLECTION_JSON_PATH, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        f.write('\n')
    print('   Collection JSON updated')


def git_push(version):
    print('6. Pushing to GitHub...')
    box_dir = os.path.normpath(os.path.join(SCRIPT_DIR, '..'))
    ret = os.system(
        f'cd "{box_dir}" && git add RiverBox.json RiverBox release.py '
        f'&& git commit -m "Release RiverBox {version}" && git push origin main'
    )
    if ret != 0:
        print('   Warning: push of River repo failed')

    if os.path.exists(COLLECTION_JSON_PATH):
        collection_dir = os.path.dirname(COLLECTION_JSON_PATH)
        ret = os.system(
            f'cd "{collection_dir}" && git add plugins.json '
            f'&& git commit -m "Update RiverBox to {version}" && git push origin main'
        )
        if ret != 0:
            print('   Warning: push of PluginRepo failed')


def main():
    if not RIVER_TOKEN:
        print('Error: set RIVER_TOKEN (or GH_TOKEN) environment variable')
        sys.exit(1)

    version = get_version()
    print(f'=== RiverBox Release {version} ===\n')

    build()
    package(version)
    download_url = create_release(RIVER_TOKEN, version)
    update_manifest_json(version, download_url)
    update_collection_json(version, download_url)
    git_push(version)

    print(f'\n=== Done! ===')
    print(f'Install URL: https://raw.githubusercontent.com/{REPO}/main/RiverBox.json')
    print(f'Collection URL: https://raw.githubusercontent.com/{COLLECTION_REPO}/main/plugins.json')


if __name__ == '__main__':
    main()
