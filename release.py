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
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CSPROJ_PATH = os.path.join(SCRIPT_DIR, 'RiverBox', 'RiverBox.csproj')
ZIP_PATH = os.path.join(SCRIPT_DIR, 'release', 'RiverBox.zip')
JSON_PATH = os.path.join(SCRIPT_DIR, 'RiverBox.json')
ICON_PATH = os.path.join(SCRIPT_DIR, 'RiverBox', 'icon.png')

def get_version():
    tree = ET.parse(CSPROJ_PATH)
    root = tree.getroot()
    ns = '{http://schemas.microsoft.com/developer/msbuild/2003}'
    for elem in root.iter(f'{ns}Version'):
        return elem.text
    raise Exception('Version not found in csproj')

def build():
    print('1. Building...')
    ret = os.system(f'dotnet build -c Release -p:ProjectDir="{os.path.join(SCRIPT_DIR, "RiverBox")}"')
    if ret != 0:
        print('Build failed!')
        sys.exit(1)

def package(version):
    print('2. Packaging...')
    os.makedirs(os.path.dirname(ZIP_PATH), exist_ok=True)
    build_dir = os.path.join(SCRIPT_DIR, 'RiverBox', 'bin', 'Release')
    with zipfile.ZipFile(ZIP_PATH, 'w', zipfile.ZIP_DEFLATED) as zipf:
        zipf.write(os.path.join(build_dir, 'RiverBox.dll'), 'RiverBox.dll')
        zipf.write(os.path.join(build_dir, 'ECommons.dll'), 'ECommons.dll')
        zipf.write(os.path.join(build_dir, 'RiverBox.json'), 'RiverBox.json')
        zipf.write(os.path.join(build_dir, 'RiverBox.deps.json'), 'RiverBox.deps.json')
        zipf.write(ICON_PATH, 'icon.png')
    print(f'   Created {ZIP_PATH}')

def create_release(token, version):
    print(f'3. Creating release {version}...')
    ctx = ssl.create_default_context()

    data = json.dumps({
        'tag_name': version,
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

def update_json(version, download_url):
    print('4. Updating RiverBox.json...')
    with open(JSON_PATH, 'r', encoding='utf-8') as f:
        data = json.load(f)
    data[0]['AssemblyVersion'] = version
    data[0]['DownloadLinkInstall'] = download_url
    data[0]['DownloadLinkTesting'] = download_url
    data[0]['DownloadLinkUpdate'] = download_url
    with open(JSON_PATH, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
    print('   RiverBox.json updated')

def git_push():
    print('5. Pushing to GitHub...')
    os.system('git add RiverBox.json')
    os.system(f'git commit -m "Release {get_version()}"')
    os.system('git push origin main')

def main():
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    if not token:
        print('Error: Set GH_TOKEN environment variable')
        print('  set GH_TOKEN=your_token_here')
        sys.exit(1)

    version = get_version()
    print(f'=== RiverBox Release {version} ===\n')

    build()
    package(version)
    download_url = create_release(token, version)
    update_json(version, download_url)
    git_push()

    print(f'\n=== Done! ===')
    print(f'Install URL: https://raw.githubusercontent.com/{REPO}/main/RiverBox.json')

if __name__ == '__main__':
    main()
