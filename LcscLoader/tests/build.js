#!/usr/bin/env node
// Builds tests/bin/Harness.exe from the parser sources (no AD SDK references) and
// runs the offline fixtures unless --no-run is given.
// Usage: node LcscLoader/tests/build.js [--no-run]

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const adSdk = require(path.join(__dirname, '..', '..', 'tools', 'ad-sdk'));

const root = path.resolve(__dirname, '..', '..');
const csc = path.join(root, '_tools', 'roslyn', 'tasks', 'net472', 'csc.exe');
const refsTemplate = path.join(root, '_tools', 'refs.rsp');
const refDir = path.join(root, '_tools', 'net48ref');
const outDir = path.join(__dirname, 'bin');
const exe = path.join(outDir, 'Harness.exe');

const SOURCES = [
  'LcscLoader/src/PluginLog.cs',
  'LcscLoader/src/JsonUtil.cs',
  'LcscLoader/src/EeErrors.cs',
  'LcscLoader/src/EeEndpoints.cs',
  'LcscLoader/src/EasyEdaApi.cs',
  'LcscLoader/src/EasyEdaModels.cs',
  'LcscLoader/src/SvgPath.cs',
  'LcscLoader/tests/Harness.cs',
];

function fail(message) {
  console.error('ERROR: ' + message);
  process.exit(1);
}

if (!fs.existsSync(csc)) fail('Missing compiler ' + csc + '\n  run: node tools/restore-tools.js');

// Newtonsoft comes from the AD installation (the plugin uses AD's copy as well).
function findNewtonsoft() {
  const cached = path.join(outDir, 'Newtonsoft.Json.dll');
  if (fs.existsSync(cached)) return cached;
  try {
    const sdk = adSdk.resolveSdkDir(adSdk.parseArgs([]));
    const candidate = path.join(sdk.dir, 'Newtonsoft.Json.dll');
    if (fs.existsSync(candidate)) return candidate;
  } catch {
    // fall through
  }
  return null;
}

const newtonsoft = findNewtonsoft();
if (!newtonsoft) fail('找不到 Newtonsoft.Json.dll（需要 Altium SDK 或 tests/bin 下的副本）');
if (newtonsoft !== path.join(outDir, 'Newtonsoft.Json.dll')) {
  fs.mkdirSync(outDir, { recursive: true });
  fs.copyFileSync(newtonsoft, path.join(outDir, 'Newtonsoft.Json.dll'));
}

// Expand refs.rsp the same way build.js does: repo-relative entries live in net48ref.
const refsPath = path.join(outDir, 'refs.rsp');
const refLines = fs.readFileSync(refsTemplate, 'utf8')
  .split(/\r?\n/)
  .map(line => line.trim())
  .filter(Boolean)
  .map(line => {
    const match = line.match(/^\/r:(?:"([^"]+)"|(.+))$/);
    if (!match) fail('Unrecognized line in refs.rsp: ' + line);
    const target = match[1] || match[2];
    if (/^[A-Za-z]:/.test(target) || target.startsWith('/')) return line;
    const full = path.join(refDir, target.split(/[\\/]/).join(path.sep));
    if (!fs.existsSync(full)) fail('Missing reference assembly ' + full + '\n  run: node tools/restore-tools.js');
    return '/r:"' + full + '"';
  });
fs.writeFileSync(refsPath, refLines.concat('/r:"' + path.join(outDir, 'Newtonsoft.Json.dll') + '"').join('\r\n') + '\r\n', 'utf8');

const args = [
  '-nologo',
  '-target:exe',
  '-out:' + exe,
  '-langversion:7.3',
  '@' + refsPath,
  ...SOURCES.map(file => path.join(root, file)),
];

const build = spawnSync(csc, args, { stdio: 'inherit' });
if (build.error) fail(build.error.message);
if (build.status !== 0) process.exit(build.status);
console.log('Built ' + exe);

if (process.argv.includes('--no-run')) process.exit(0);

const run = spawnSync(exe, ['--offline'], { stdio: 'inherit', cwd: root });
if (run.error) fail(run.error.message);
process.exit(run.status === null ? 1 : run.status);
