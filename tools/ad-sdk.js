'use strict';
// Resolves the Altium Designer SDK directory without assuming a fixed install path.
// Order: --sdk-dir  >  AD_SDK_DIR  >  auto-detection (registry, common install dirs,
// bounded scan of fixed drives). Never silently falls back to a wrong version.

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const REQUIRED_DLLS = ['Altium.SDK.dll', 'Altium.SDK.Interfaces.dll', 'Newtonsoft.Json.dll'];
const LEGACY_DIR = 'D:\\AD\\AD20\\System'; // last-resort candidate, kept for backward compatibility

function parseArgs(argv) {
  const args = { sdkDir: null, adVersion: null, rest: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--sdk-dir') {
      args.sdkDir = argv[++i] || null;
      if (!args.sdkDir) throw new Error('--sdk-dir requires a directory argument');
    } else if (a.startsWith('--sdk-dir=')) {
      args.sdkDir = a.slice('--sdk-dir='.length);
    } else if (a === '--ad-version') {
      args.adVersion = argv[++i] || null;
      if (!args.adVersion) throw new Error('--ad-version requires a value (e.g. 20, 24, 25)');
    } else if (a.startsWith('--ad-version=')) {
      args.adVersion = a.slice('--ad-version='.length);
    } else {
      args.rest.push(a);
    }
  }
  return args;
}

function versionFromPath(dir) {
  const m = dir.match(/[\\/](?:AD|Altium Designer)(\d{2})(?:[\\/]|$)/i);
  return m ? m[1] : null;
}

function hasSdk(dir) {
  if (!dir) return false;
  try {
    return REQUIRED_DLLS.every(name => fs.statSync(path.join(dir, name)).isFile());
  } catch {
    return false;
  }
}

function missingDlls(dir) {
  if (!dir) return REQUIRED_DLLS.slice();
  return REQUIRED_DLLS.filter(name => {
    try {
      return !fs.statSync(path.join(dir, name)).isFile();
    } catch {
      return true;
    }
  });
}

function norm(dir) {
  return path.resolve(dir).replace(/[\\/]+$/, '');
}

function fixedDrives() {
  const drives = [];
  for (let c = 65; c <= 90; c++) {
    const root = String.fromCharCode(c) + ':\\';
    try {
      if (fs.statSync(root).isDirectory()) drives.push(root);
    } catch {
      // drive not present
    }
  }
  return drives;
}

function childDirs(dir) {
  try {
    return fs.readdirSync(dir, { withFileTypes: true })
      .filter(e => e.isDirectory())
      .map(e => e.name);
  } catch {
    return [];
  }
}

function addCandidate(list, dir, source) {
  if (!dir) return;
  let resolved;
  try {
    resolved = norm(dir);
  } catch {
    return;
  }
  if (list.some(c => c.dir.toLowerCase() === resolved.toLowerCase())) return;
  list.push({ dir: resolved, source, version: versionFromPath(resolved) });
}

function registryCandidates(list) {
  if (process.platform !== 'win32') return;
  const script = [
    "$out=@();",
    "$keys=@('HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall','HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall');",
    "foreach($k in $keys){ if(Test-Path $k){ Get-ChildItem $k -ErrorAction SilentlyContinue | ForEach-Object {",
    "  $p=Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue;",
    "  if($p -and $p.DisplayName -match 'Altium Designer'){ $out += [pscustomobject]@{ name=$p.DisplayName; loc=$p.InstallLocation; icon=$p.DisplayIcon } } } } };",
    "$out | ConvertTo-Json -Compress -Depth 3",
  ].join(' ');
  try {
    const raw = execFileSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', script], {
      encoding: 'utf8',
      timeout: 15000,
      windowsHide: true,
    }).trim();
    if (!raw) return;
    const entries = JSON.parse(raw);
    const list2 = Array.isArray(entries) ? entries : [entries];
    for (const entry of list2) {
      const loc = (entry.loc || '').replace(/\//g, '\\').replace(/[\\]+$/, '');
      if (loc) addCandidate(list, path.join(loc, 'System'), 'registry');
      const icon = (entry.icon || '').replace(/^"|"$/g, '').replace(/,.*$/, '');
      if (icon && /\.exe$/i.test(icon)) addCandidate(list, path.dirname(icon), 'registry');
      const nameVersion = (entry.name || '').match(/Altium Designer\s*(\d{2})/i);
      if (loc && nameVersion) addCandidate(list, path.join(loc, 'System'), 'registry (AD' + nameVersion[1] + ')');
    }
  } catch {
    // registry probing is best-effort only
  }
}

function commonCandidates(list) {
  const roots = [process.env['ProgramFiles'], process.env['ProgramFiles(x86)']]
    .filter(Boolean)
    .map(p => path.join(p, 'Altium'));
  for (const root of roots) {
    for (const name of childDirs(root)) addCandidate(list, path.join(root, name, 'System'), 'common');
  }

  const patterns = [
    d => path.join(d, 'AD'),
    d => path.join(d, 'Altium'),
    d => path.join(d, 'Program Files', 'Altium'),
    d => path.join(d, 'Program Files (x86)', 'Altium'),
  ];
  for (const drive of fixedDrives()) {
    for (const make of patterns) {
      const parent = make(drive);
      for (const name of childDirs(parent)) addCandidate(list, path.join(parent, name, 'System'), 'scan');
    }
  }
}

function candidates() {
  const list = [];
  const env = process.env.AD_SDK_DIR;
  if (env) addCandidate(list, env, 'AD_SDK_DIR');
  registryCandidates(list);
  commonCandidates(list);
  addCandidate(list, LEGACY_DIR, 'legacy');
  return list;
}

function describe(list) {
  if (!list.length) return '  (no candidates found)';
  return list
    .map(c => {
      const missing = missingDlls(c.dir);
      const state = missing.length === 0 ? 'ok' : 'missing: ' + missing.join(', ');
      return '  - ' + c.dir + '  [' + c.source + (c.version ? ', AD' + c.version : '') + ']  ' + state;
    })
    .join('\n');
}

function matchesVersion(candidate, adVersion) {
  if (!adVersion) return true;
  if (candidate.version) return candidate.version === adVersion;
  return false;
}

// Resolve the SDK directory. Throws with a candidate report when nothing usable is found.
function resolveSdkDir(opts = {}) {
  if (opts.sdkDir) {
    const dir = norm(opts.sdkDir);
    if (!fs.existsSync(dir)) {
      throw new Error('SDK directory does not exist: ' + dir);
    }
    const missing = missingDlls(dir);
    if (missing.length) {
      throw new Error('SDK directory is missing required assemblies: ' + missing.join(', ') + '\n  in ' + dir);
    }
    if (opts.adVersion && versionFromPath(dir) !== opts.adVersion) {
      throw new Error('SDK directory does not look like AD' + opts.adVersion + ': ' + dir);
    }
    return { dir, version: versionFromPath(dir), source: '--sdk-dir' };
  }

  const all = candidates();
  const usable = all.filter(c => hasSdk(c.dir));
  const filtered = usable.filter(c => matchesVersion(c, opts.adVersion));

  if (filtered.length === 1) {
    return { dir: filtered[0].dir, version: filtered[0].version, source: filtered[0].source };
  }

  if (filtered.length === 0) {
    const hint = opts.adVersion ? ' for AD' + opts.adVersion : '';
    throw new Error(
      'No usable Altium SDK directory found' + hint + '. Pass --sdk-dir <dir> or set AD_SDK_DIR.\n' +
      'Candidates checked:\n' + describe(all)
    );
  }

  throw new Error(
    'Multiple AD installations found' + (opts.adVersion ? ' for AD' + opts.adVersion : '') +
    '. Pick one with --sdk-dir <dir> or --ad-version <20|24|25>.\nCandidates:\n' + describe(filtered)
  );
}

module.exports = { REQUIRED_DLLS, parseArgs, resolveSdkDir, candidates, describe, hasSdk, missingDlls, versionFromPath };
