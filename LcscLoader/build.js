#!/usr/bin/env node
// Builds LcscLoader.dll against a resolved Altium Designer SDK.
// Usage: node build.js [--sdk-dir <dir>] [--ad-version <20|24|25>]
// Resolution order: --sdk-dir > AD_SDK_DIR > auto-detection. See tools/ad-sdk.js.

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const adSdk = require(path.join(__dirname, '..', 'tools', 'ad-sdk'));

const root = path.resolve(__dirname, '..');
const csc = path.join(root, '_tools', 'roslyn', 'tasks', 'net472', 'csc.exe');
const refs = path.join(root, '_tools', 'refs.rsp');
const outDir = path.join(__dirname, 'bin');

let cli;
let sdk;
try {
  cli = adSdk.parseArgs(process.argv.slice(2));
  sdk = adSdk.resolveSdkDir(cli);
} catch (error) {
  console.error('ERROR: ' + error.message);
  process.exit(1);
}
const sdkDir = sdk.dir;
console.log('SDK: ' + sdkDir + '  [' + sdk.source + (sdk.version ? ', AD' + sdk.version : '') + ']');
const dll = path.join(outDir, 'LcscLoader.dll');
const sourceDir = path.join(__dirname, 'src');
const uiDir = path.join(root, 'LcscStudio', 'src');

// Explicit exclusions only; everything else is discovered by scanning, so new files
// are picked up automatically. Paths are relative to the repository root.
const EXCLUDED_SOURCES = {
  'LcscLoader/src/MainForm.cs': 'dead code (removed in T4-6)',
  'LcscStudio/src/PanelControl.cs': 'WPF UI (dropped by D3)',
  'LcscStudio/src/Preview2D.cs': 'WPF UI (dropped by D3)',
  'LcscStudio/src/Preview3D.cs': 'WPF UI (dropped by D3)',
  'LcscStudio/src/PreviewFootprint.cs': 'WPF UI (dropped by D3)',
  'LcscStudio/src/SettingsWindow.cs': 'WPF UI (dropped by D3)',
  'LcscStudio/src/TargetWindow.cs': 'WPF UI (dropped by D3)',
  'LcscStudio/src/UiTheme.cs': 'WPF UI (dropped by D3)',
};

function relativeToRoot(file) {
  return path.relative(root, file).split(path.sep).join('/');
}

function walkCs(dir) {
  const result = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) result.push(...walkCs(full));
    else if (entry.isFile() && entry.name.endsWith('.cs')) result.push(full);
  }
  return result;
}

const sources = [...walkCs(sourceDir), ...walkCs(uiDir)]
  .filter(file => !(relativeToRoot(file) in EXCLUDED_SOURCES))
  .sort((a, b) => relativeToRoot(a).localeCompare(relativeToRoot(b), 'en'));

console.log('sources: ' + sources.length + ' files');
for (const file of sources) console.log('  ' + relativeToRoot(file));

if (cli.rest.includes('--list-sources')) {
  process.exit(0);
}

fs.mkdirSync(outDir, { recursive: true });

const args = [
  '-nologo',
  '-target:library',
  '-out:' + dll,
  '-langversion:7.3',
  '@' + refs,
  '/r:' + path.join(sdkDir, 'Altium.SDK.dll'),
  '/r:' + path.join(sdkDir, 'Altium.SDK.Interfaces.dll'),
  '/r:' + path.join(sdkDir, 'Newtonsoft.Json.dll'),
  ...sources,
];

const result = spawnSync(csc, args, { stdio: 'inherit' });
if (result.error) {
  console.error('ERROR: ' + result.error.message);
  process.exit(1);
}
if (result.status !== 0) {
  process.exit(result.status);
}
console.log('Built ' + dll);
