#!/usr/bin/env node
// Builds LcscLoader.dll against the local AD20 SDK. Run: node build.js

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const csc = path.join(root, '_tools', 'roslyn', 'tasks', 'net472', 'csc.exe');
const refs = path.join(root, '_tools', 'refs.rsp');
const sdkDir = 'D:\\AD\\AD20\\System';
const outDir = path.join(__dirname, 'bin');
const dll = path.join(outDir, 'LcscLoader.dll');
const sourceDir = path.join(__dirname, 'src');
const uiDir = path.join(root, 'LcscStudio', 'src');

const uiFiles = [
  'SearchClient.cs',
  'PartCache.cs',
  'Settings.cs',
  'wf/PreviewCanvas.cs',
  'wf/PanelForm.cs',
  'wf/TargetForm.cs',
  'wf/SettingsForm.cs',
];

const sources = [
  ...fs.readdirSync(sourceDir)
    .filter(name => name.endsWith('.cs'))
    .map(name => path.join(sourceDir, name)),
  ...uiFiles.map(name => path.join(uiDir, name)),
];

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
