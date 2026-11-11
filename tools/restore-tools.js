'use strict';
// Restores the local build toolchain (_tools/roslyn and _tools/net48ref).
// Local *.nupkg files are used first (works offline); otherwise packages are
// downloaded from NuGet and verified against pinned SHA256 before extraction.

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const root = path.resolve(__dirname, '..');
const toolsDir = path.join(root, '_tools');

const PACKAGES = [
  {
    name: 'roslyn',
    id: 'microsoft.net.compilers.toolset',
    version: '4.13.0',
    sha256: '03A9D9D52540B98F6DD823527D2265D66799600CD5EE93D9DADAC8A301A6AA8C',
    marker: path.join('tasks', 'net472', 'csc.exe'),
  },
  {
    name: 'net48ref',
    id: 'microsoft.netframework.referenceassemblies.net48',
    version: '1.0.3',
    sha256: '8A7E348538E7EB91351696911689F49E3D4F63F8BAB517432BBE159B8B1104A2',
    marker: path.join('build', '.NETFramework', 'v4.8', 'mscorlib.dll'),
  },
];

function log(message) {
  console.log(message);
}

function sha256(file) {
  const crypto = require('crypto');
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase();
}

function isRestored(pkg) {
  return fs.existsSync(path.join(toolsDir, pkg.name, pkg.marker));
}

function downloadUrl(pkg) {
  return 'https://api.nuget.org/v3-flatcontainer/' + pkg.id + '/' + pkg.version + '/' +
    pkg.id + '.' + pkg.version + '.nupkg';
}

async function ensureNupkg(pkg, options) {
  const local = path.join(toolsDir, pkg.name + '.nupkg');
  if (fs.existsSync(local)) {
    const actual = sha256(local);
    if (actual !== pkg.sha256) {
      throw new Error('Local package checksum mismatch: ' + local +
        '\n  expected ' + pkg.sha256 + '\n  actual   ' + actual);
    }
    log('  package: local ' + path.basename(local));
    return local;
  }

  if (options.offline) {
    throw new Error('Offline mode: ' + local + ' is missing. Cannot download ' + pkg.id + '@' + pkg.version);
  }

  const url = downloadUrl(pkg);
  log('  package: download ' + url);
  const response = await fetch(url, { signal: AbortSignal.timeout(120000) });
  if (!response.ok) {
    throw new Error('Download failed (HTTP ' + response.status + '): ' + url);
  }
  const buffer = Buffer.from(await response.arrayBuffer());
  const actual = require('crypto').createHash('sha256').update(buffer).digest('hex').toUpperCase();
  if (actual !== pkg.sha256) {
    throw new Error('Downloaded package checksum mismatch: ' + url +
      '\n  expected ' + pkg.sha256 + '\n  actual   ' + actual);
  }
  fs.writeFileSync(local, buffer);
  log('  package: saved ' + local + ' (sha256 verified)');
  return local;
}

function extractNupkg(nupkgPath, destination) {
  const temp = destination + '.tmp-' + Date.now();
  fs.rmSync(temp, { recursive: true, force: true });
  fs.mkdirSync(temp, { recursive: true });

  let result = spawnSync('tar', ['-xf', nupkgPath, '-C', temp], { encoding: 'utf8' });
  if (result.error || result.status !== 0) {
    const script = 'Expand-Archive -LiteralPath ' + JSON.stringify(nupkgPath) +
      ' -DestinationPath ' + JSON.stringify(temp) + ' -Force';
    result = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8' });
  }
  if (result.error || result.status !== 0) {
    fs.rmSync(temp, { recursive: true, force: true });
    throw new Error('Failed to extract ' + nupkgPath + ': ' + (result.error || result.stderr || '').toString().trim());
  }

  if (fs.existsSync(destination)) {
    const backup = destination + '.bak-' + Date.now();
    fs.renameSync(destination, backup);
    log('  moved existing directory to ' + backup);
  }
  fs.renameSync(temp, destination);
}

async function restorePackage(pkg, options) {
  log(pkg.name + ':');
  if (isRestored(pkg) && !options.force) {
    log('  already restored: ' + path.join(toolsDir, pkg.name));
    return;
  }

  const nupkg = await ensureNupkg(pkg, options);
  log('  extracting to ' + path.join(toolsDir, pkg.name));
  extractNupkg(nupkg, path.join(toolsDir, pkg.name));

  if (!isRestored(pkg)) {
    throw new Error('Extraction completed but ' + pkg.marker + ' is missing in ' +
      path.join(toolsDir, pkg.name));
  }
  log('  ok');
}

function refsTemplatePath() {
  return path.join(toolsDir, 'refs.rsp');
}

// refs.rsp stores paths relative to _tools/net48ref so it stays portable.
function checkReferences() {
  const net48ref = path.join(toolsDir, 'net48ref');
  const template = refsTemplatePath();
  if (!fs.existsSync(template)) throw new Error('Missing ' + template);

  const lines = fs.readFileSync(template, 'utf8').split(/\r?\n/).map(s => s.trim()).filter(Boolean);
  const missing = [];
  let absolute = 0;
  for (const line of lines) {
    const match = line.match(/^\/r:(?:"([^"]+)"|(.+))$/);
    if (!match) throw new Error('Unrecognized line in refs.rsp: ' + line);
    const target = (match[1] || match[2]).replace(/\\/g, '/');
    if (/^[A-Za-z]:/i.test(target) || target.startsWith('/')) {
      absolute++;
      if (!fs.existsSync(target)) missing.push(target);
      continue;
    }
    const full = path.join(net48ref, target.split('/').join(path.sep));
    if (!fs.existsSync(full)) missing.push(full);
  }

  if (absolute > 0) {
    log('  warning: refs.rsp still contains ' + absolute + ' absolute path(s); it is not portable');
  }
  if (missing.length) {
    throw new Error('Missing ' + missing.length + ' reference assemblies, e.g. ' + missing[0] +
      '\n  run: node tools/restore-tools.js');
  }
  log('  references: ' + lines.length + ' entries verified');
}

function cscVersion() {
  const csc = path.join(toolsDir, 'roslyn', 'tasks', 'net472', 'csc.exe');
  if (!fs.existsSync(csc)) throw new Error('Missing ' + csc + ' (run: node tools/restore-tools.js)');
  const result = spawnSync(csc, ['-version'], { encoding: 'utf8', windowsHide: true });
  const text = (result.stdout || result.stderr || '').trim();
  if (result.status !== 0) throw new Error('csc.exe -version failed: ' + text);
  return text;
}

async function main() {
  const options = {
    offline: process.argv.includes('--offline'),
    force: process.argv.includes('--force'),
    check: process.argv.includes('--check'),
  };

  log('toolchain root: ' + toolsDir + (options.offline ? ' (offline)' : ''));
  for (const pkg of PACKAGES) {
    if (options.check) {
      log(pkg.name + ': ' + (isRestored(pkg) ? 'ok' : 'MISSING'));
    } else {
      await restorePackage(pkg, options);
    }
  }
  checkReferences();
  log('compiler: ' + cscVersion());
  log('result: ' + (options.check ? 'checked' : 'restored'));

  if (options.check) {
    const ok = PACKAGES.every(isRestored);
    process.exitCode = ok ? 0 : 1;
  }
}

main().catch(error => {
  console.error('ERROR: ' + error.message);
  process.exit(1);
});
