'use strict';
// Shared deployment-target logic for deploy.js and verify-deploy.js.
// Two supported layouts:
//   system      - copy next to the AD installation (<sdk dir>), which is what AD20
//                 on this machine actually loads (D:\AD\AD20\System).
//   programdata - classic extension folder + ExtensionsRegistry.xml entry.

const fs = require('fs');
const os = require('os');
const path = require('path');

const LAYOUTS = ['auto', 'system', 'programdata'];

const FILES = {
  dll: { source: ['bin', 'LcscLoader.dll'], name: 'LcscLoader.dll' },
  ins: { source: ['LCSCLoader.Ins'], name: 'LCSCLoader.Ins' },
  rcs: { source: ['LcscLoader.rcs'], name: 'LCSCLoader.rcs' },
};

function programDataRoot() {
  return path.join(process.env.ProgramData || 'C:\\ProgramData', 'Altium');
}

function extensionDirs() {
  const root = programDataRoot();
  if (!fs.existsSync(root)) return [];
  return fs.readdirSync(root)
    .map(name => path.join(root, name, 'Extensions'))
    .filter(dir => fs.existsSync(path.join(dir, 'ExtensionsRegistry.xml')))
    .map(dir => ({
      dir,
      registry: path.join(dir, 'ExtensionsRegistry.xml'),
      version: (path.basename(path.dirname(dir)).match(/Altium Designer\s*(\d+)/i) || [])[1] || null,
    }));
}

function pickExtensionDir(layout, adVersion) {
  const all = extensionDirs();
  if (!all.length) {
    throw new Error('No Altium Designer data folder with ExtensionsRegistry.xml under ' + programDataRoot());
  }
  const matches = adVersion ? all.filter(entry => entry.version === adVersion) : all;
  if (matches.length === 1) return matches[0];
  if (!matches.length) {
    throw new Error('No ProgramData extension folder for AD' + adVersion + '. Candidates:\n  ' +
      all.map(entry => entry.dir + ' [AD' + (entry.version || '?') + ']').join('\n  '));
  }
  throw new Error('Multiple Altium data folders found. Pass --ad-version to choose:\n  ' +
    matches.map(entry => entry.dir + ' [AD' + (entry.version || '?') + ']').join('\n  '));
}

// sdk is the resolved result from tools/ad-sdk.js (may be null when layout is programdata).
function resolveTarget(options = {}) {
  const layout = options.layout || 'auto';
  if (!LAYOUTS.includes(layout)) {
    throw new Error('Unknown layout "' + layout + '". Use one of: ' + LAYOUTS.join(', '));
  }

  if (layout === 'system' || (layout === 'auto' && options.preferSystem)) {
    if (!options.sdk) {
      throw new Error('System layout needs a resolved SDK directory (--sdk-dir or auto-detection).');
    }
    return { layout: 'system', dir: options.sdk.dir, registry: null, sdk: options.sdk };
  }

  if (layout === 'programdata') {
    const entry = pickExtensionDir(layout, options.adVersion);
    return {
      layout: 'programdata',
      dir: path.join(entry.dir, 'LCSCLoader'),
      registry: entry.registry,
      extensionsDir: entry.dir,
      sdk: options.sdk || null,
    };
  }

  // auto: prefer the layout that already holds a deployed DLL, otherwise ProgramData.
  if (options.sdk && fs.existsSync(path.join(options.sdk.dir, FILES.dll.name))) {
    return { layout: 'system', dir: options.sdk.dir, registry: null, sdk: options.sdk };
  }
  const entry = pickExtensionDir('programdata', options.adVersion);
  return {
    layout: 'programdata',
    dir: path.join(entry.dir, 'LCSCLoader'),
    registry: entry.registry,
    extensionsDir: entry.dir,
    sdk: options.sdk || null,
  };
}

function sourcePaths(projectRoot) {
  return Object.entries(FILES).map(([key, file]) => ({
    key,
    name: file.name,
    source: path.join(projectRoot, ...file.source),
  }));
}

function describeTargets(projectRoot, adVersion) {
  const lines = [];
  const entries = extensionDirs();
  lines.push('ProgramData data folders: ' + (entries.length ? '' : 'none'));
  for (const entry of entries) {
    const target = path.join(entry.dir, 'LCSCLoader');
    lines.push('  ' + target + '  [AD' + (entry.version || '?') + ']' +
      (fs.existsSync(path.join(target, FILES.dll.name)) ? '  (deployed)' : ''));
  }
  const sdkCandidates = require('./ad-sdk').candidates().filter(c => require('./ad-sdk').hasSdk(c.dir));
  lines.push('SDK candidates: ' + (sdkCandidates.length ? '' : 'none'));
  for (const candidate of sdkCandidates) {
    lines.push('  ' + candidate.dir + '  [AD' + (candidate.version || '?') + ']' +
      (fs.existsSync(path.join(candidate.dir, FILES.dll.name)) ? '  (deployed)' : ''));
  }
  return lines.join(os.EOL);
}

module.exports = { FILES, LAYOUTS, resolveTarget, extensionDirs, sourcePaths, describeTargets };
