#!/usr/bin/env node
// Builds installer/bin/LcscSetup.exe with the plugin payload embedded as resources.
// Run after `node LcscLoader/build.js`.
//
// Usage: node installer/build.js

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const root = path.resolve(__dirname, '..');
const csc = path.join(root, '_tools', 'roslyn', 'tasks', 'net472', 'csc.exe');
const refsTemplate = path.join(root, '_tools', 'refs.rsp');
const net48RefDir = path.join(root, '_tools', 'net48ref');
const outDir = path.join(__dirname, 'bin');
const prepDir = path.join(outDir, 'prep');
const exe = path.join(outDir, 'LcscSetup.exe');

function fail(message) {
  console.error('ERROR: ' + message);
  process.exit(1);
}

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase();
}

const payloadFiles = [
  { name: 'LcscLoader.dll', source: path.join(root, 'LcscLoader', 'bin', 'LcscLoader.dll') },
  { name: 'LCSCLoader.Ins', source: path.join(root, 'LcscLoader', 'bin', 'LCSCLoader.Ins') },
  { name: 'LCSCLoader.rcs', source: path.join(root, 'LcscLoader', 'LCSCLoader.rcs') },
];

for (const file of payloadFiles) {
  if (!fs.existsSync(file.source)) {
    fail('缺少 ' + file.source + '\n  先运行 node LcscLoader/build.js');
  }
}

const buildInfoPath = path.join(root, 'LcscLoader', 'bin', 'build-info.json');
if (!fs.existsSync(buildInfoPath)) fail('缺少 ' + buildInfoPath);
const buildInfo = JSON.parse(fs.readFileSync(buildInfoPath, 'utf8'));
const version = String(buildInfo.version || '').trim();
if (!version) fail('build-info.json 没有 version 字段');

fs.mkdirSync(prepDir, { recursive: true });
const payloadInfoPath = path.join(prepDir, 'payload.info');
const builtAt = String(buildInfo.builtAtUtc || '').slice(0, 10);
fs.writeFileSync(payloadInfoPath, [
  'version=' + version,
  'built=' + builtAt,
  'dllSha256=' + sha256(payloadFiles[0].source),
  'insSha256=' + sha256(payloadFiles[1].source),
  'rcsSha256=' + sha256(payloadFiles[2].source),
  '',
].join('\n'), 'utf8');

// refs.rsp keeps repo-relative paths; csc gets absolute ones in a generated file.
function expandRefs() {
  if (!fs.existsSync(csc)) fail('Missing compiler ' + csc + '\n  run: node tools/restore-tools.js');
  if (!fs.existsSync(refsTemplate)) fail('Missing ' + refsTemplate);

  const lines = fs.readFileSync(refsTemplate, 'utf8')
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean);

  const expanded = lines.map(line => {
    const match = line.match(/^\/r:(?:"([^"]+)"|(.+))$/);
    if (!match) fail('Unrecognized line in refs.rsp: ' + line);
    const target = match[1] || match[2];
    if (/^[A-Za-z]:/.test(target) || target.startsWith('/')) return line;
    const full = path.join(net48RefDir, target.split(/[\\/]/).join(path.sep));
    if (!fs.existsSync(full)) fail('Missing reference assembly ' + full);
    return '/r:"' + full + '"';
  });

  const file = path.join(prepDir, 'refs.rsp');
  fs.writeFileSync(file, expanded.join('\r\n') + '\r\n', 'utf8');
  return file;
}

const refs = expandRefs();
const sources = fs.readdirSync(path.join(__dirname, 'src'))
  .filter(name => name.endsWith('.cs'))
  .sort()
  .map(name => path.join(__dirname, 'src', name));

const args = [
  '-nologo',
  '-target:winexe',
  '-platform:anycpu',
  '-optimize+',
  '-langversion:7.3',
  '-deterministic',
  '-out:' + exe,
  '-win32manifest:' + path.join(__dirname, 'src', 'app.manifest'),
  '@' + refs,
];

for (const file of payloadFiles) {
  args.push('/resource:' + file.source + ',payload.' + file.name);
}
args.push('/resource:' + payloadInfoPath + ',payload.payload.info');
args.push(...sources);

const result = spawnSync(csc, args, { stdio: 'inherit' });
if (result.error) fail(result.error.message);
if (result.status !== 0) process.exit(result.status);

console.log('Built ' + exe);
console.log('  version ' + version + '  payload dll ' + sha256(payloadFiles[0].source).slice(0, 12) + '…');
console.log('  size    ' + fs.statSync(exe).size + ' bytes');
