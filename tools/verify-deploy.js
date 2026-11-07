#!/usr/bin/env node
// Checks that what Altium Designer will actually load matches the current build output.
//
// Usage: node tools/verify-deploy.js [--target auto|system|programdata]
//                                    [--sdk-dir <dir>] [--ad-version <20|24|25>]

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const adSdk = require('./ad-sdk');
const deployTarget = require('./deploy-target');

const root = path.resolve(__dirname, '..');
const projectRoot = path.join(root, 'LcscLoader');

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase();
}

function flagValue(args, name, fallback) {
  const index = args.indexOf(name);
  if (index < 0) return fallback;
  const value = args[index + 1];
  if (!value || value.startsWith('--')) throw new Error(name + ' requires a value');
  return value;
}

function main() {
  const cli = adSdk.parseArgs(process.argv.slice(2));
  const layout = flagValue(cli.rest, '--target', 'auto');

  const expectedDll = path.join(projectRoot, 'bin', 'LcscLoader.dll');
  if (!fs.existsSync(expectedDll)) {
    throw new Error('No build output at ' + expectedDll + ' (run node LcscLoader/build.js)');
  }
  const expected = {
    dll: sha256(expectedDll),
    ins: sha256(deployTarget.sourcePaths(projectRoot).find(f => f.key === 'ins').source),
    rcs: sha256(deployTarget.sourcePaths(projectRoot).find(f => f.key === 'rcs').source),
  };

  let buildInfo = null;
  const buildInfoPath = path.join(projectRoot, 'bin', 'build-info.json');
  if (fs.existsSync(buildInfoPath)) {
    buildInfo = JSON.parse(fs.readFileSync(buildInfoPath, 'utf8'));
  }

  let sdk = null;
  try {
    sdk = adSdk.resolveSdkDir(cli);
  } catch (error) {
    if (layout !== 'programdata') throw error;
    console.warn('warning: no SDK detected (' + error.message.split('\n')[0] + ')');
  }

  const target = deployTarget.resolveTarget({
    layout,
    sdk,
    adVersion: cli.adVersion,
    preferSystem: true,
  });

  console.log('expected: ' + path.relative(root, expectedDll) + '  ' + expected.dll);
  if (buildInfo) console.log('build-info: ' + buildInfo.builtAtUtc + '  commit ' +
    (buildInfo.git && buildInfo.git.commit ? buildInfo.git.commit.slice(0, 7) : 'unknown'));
  console.log('layout: ' + target.layout);
  console.log('target: ' + target.dir);

  const failures = [];
  const files = deployTarget.sourcePaths(projectRoot);
  const expectedHashes = { dll: expected.dll, ins: expected.ins, rcs: expected.rcs };
  for (const file of files) {
    const deployed = path.join(target.dir, file.name);
    if (!fs.existsSync(deployed)) {
      failures.push(file.name + ': not deployed');
      console.log('  [missing] ' + file.name);
      continue;
    }
    const actual = sha256(deployed);
    const ok = actual === expectedHashes[file.key];
    if (!ok) failures.push(file.name + ': hash mismatch (' + actual + ' != ' + expectedHashes[file.key] + ')');
    console.log('  [' + (ok ? 'ok' : 'STALE') + '] ' + file.name + '  ' + actual);
  }

  const deployedIns = path.join(target.dir, 'LCSCLoader.Ins');
  if (fs.existsSync(deployedIns)) {
    const match = fs.readFileSync(deployedIns, 'utf8').match(/EditorExePath\s*=\s*'([^']+)'/);
    const exe = match ? match[1] : null;
    const ok = exe === 'LcscLoader.dll';
    if (!ok) failures.push('LCSCLoader.Ins: EditorExePath is ' + (exe || 'missing'));
    console.log('  [' + (ok ? 'ok' : 'BAD') + '] EditorExePath = ' + (exe || 'missing'));
  }

  if (buildInfo && buildInfo.output && buildInfo.output.sha256 !== expected.dll) {
    failures.push('build-info.json output hash does not match bin/LcscLoader.dll');
    console.log('  [BAD] build-info output ' + buildInfo.output.sha256);
  }

  if (target.layout === 'system') {
    const programData = deployTarget.extensionDirs();
    if (programData.length) {
      for (const entry of programData) {
        const deployed = path.join(entry.dir, 'LCSCLoader', 'LcscLoader.dll');
        if (fs.existsSync(deployed)) {
          console.log('info: ProgramData also holds a deployment: ' + deployed +
            ' (AD on this machine may prefer either layout)');
        }
      }
    }
  }

  if (failures.length) {
    console.log('verdict: FAIL');
    for (const failure of failures) console.log('  - ' + failure);
    process.exitCode = 1;
    return;
  }
  console.log('verdict: OK');
}

try {
  main();
} catch (error) {
  console.error('ERROR: ' + error.message);
  process.exitCode = 1;
}
