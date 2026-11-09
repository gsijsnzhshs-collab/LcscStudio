#!/usr/bin/env node
// Installs the LCSCLoader extension into a running Altium Designer installation.
// Run from an elevated shell, with Altium Designer closed.
//
// Usage:
//   node deploy.js [--target auto|system|programdata] [--sdk-dir <dir>] [--ad-version <20|22|24|25>]
//                  [--target-dir <AD System dir>] [--uninstall] [--dry-run] [--force]
//
// --target-dir skips SDK auto-detection and installs into the given directory, which is
// how a build made on one machine is deployed to another AD installation.
//
// Layouts:
//   system      - copy beside the AD binaries (this machine loads D:\AD\AD20\System)
//   programdata - copy into %ProgramData%\Altium\...\Extensions and register it

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const adSdk = require(path.join(__dirname, '..', 'tools', 'ad-sdk'));
const deployTarget = require(path.join(__dirname, '..', 'tools', 'deploy-target'));

const root = __dirname;
const argv = process.argv.slice(2);
const flags = {
  uninstall: argv.includes('--uninstall'),
  dryRun: argv.includes('--dry-run'),
  force: argv.includes('--force'),
};
let cli;
try {
  cli = adSdk.parseArgs(argv);
} catch (error) {
  console.error('ERROR: ' + error.message);
  process.exit(1);
}

function flagValue(name, fallback) {
  const index = cli.rest.indexOf(name);
  if (index < 0) return fallback;
  const value = cli.rest[index + 1];
  if (!value || value.startsWith('--')) throw new Error(name + ' requires a value');
  return value;
}

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase();
}

// The version comes from the build artifact, and the rendered .Ins must agree with it:
// a silent mismatch between registry, .Ins and DLL is the failure this prevents.
function buildVersion() {
  const buildInfoPath = path.join(root, 'bin', 'build-info.json');
  if (!fs.existsSync(buildInfoPath)) {
    throw new Error('Missing ' + buildInfoPath + ' (run node build.js first)');
  }
  const info = JSON.parse(fs.readFileSync(buildInfoPath, 'utf8'));
  const version = String(info.version || '').trim();
  if (!version) {
    throw new Error('bin/build-info.json has no "version" field (rebuild with build.js)');
  }
  const insPath = path.join(root, 'bin', 'LCSCLoader.Ins');
  if (!fs.existsSync(insPath)) {
    throw new Error('Missing ' + insPath + ' (run node build.js first)');
  }
  const match = fs.readFileSync(insPath, 'utf8').match(/Version\s*=\s*'Version ([^']+)'/);
  if (!match || match[1] !== version) {
    throw new Error('LCSCLoader.Ins version (' + (match ? match[1] : 'missing') +
      ') does not match build-info version (' + version + ')');
  }
  return version;
}

function oleDate(date) {
  return (date.getTime() - Date.UTC(1899, 11, 30)) / 86400000;
}

function buildItem(extensionPath) {
  const now = oleDate(new Date());
  const lines = [
    '  <Item HRID="LCSCLoader" Guid="B5C2A7E4-3D9F-4B1A-9C6E-8F2A1D7B3C90">',
    '    <Path>' + extensionPath + '</Path>',
    '    <Status>0</Status>',
    '    <VaultGuid></VaultGuid>',
    '    <CreatedBy>LCSC Loader</CreatedBy>',
    '    <CategoryGuid>793A1F67-0B22-4E01-A5DE-3176A1E8C60D</CategoryGuid>',
    '    <CategoryName></CategoryName>',
    '    <ReadMe></ReadMe>',
    '    <Help></Help>',
    '    <Requirements></Requirements>',
    '    <Title>LCSC Component Loader</Title>',
    '    <ShortDescription>Import symbol, footprint and 3D model by LCSC part number.</ShortDescription>',
    '    <LongDescription></LongDescription>',
    '    <SmallImage></SmallImage>',
    '    <LargeImage></LargeImage>',
    '    <Version>' + buildVersion() + '</Version>',
    '    <VersionGuid>D7E4C9A6-5FB1-4D3C-BE80-0B4C3F9D5E12</VersionGuid>',
    '    <ReleasedDate>' + now + '</ReleasedDate>',
    '    <ReleaseNotes></ReleaseNotes>',
    '    <DateInstalled>' + now + '</DateInstalled>',
    '    <PlatformVersions>',
    '      <DXP BuildNumber="1.0.10.0"/>',
    '      <EDP BuildNumber="10.0.10.0"/>',
    '      <MaxDXP BuildNumber="0.0.0.0"/>',
    '      <MaxEDP BuildNumber="0.0.0.0"/>',
    '    </PlatformVersions>',
    '  </Item>',
    '',
  ];
  return lines.join('\r\n');
}

function altiumRunning() {
  const result = spawnSync('tasklist', ['/FI', 'IMAGENAME eq X2.EXE', '/NH'], {
    encoding: 'utf8',
    windowsHide: true,
  });
  return (result.stdout || '').toUpperCase().includes('X2.EXE');
}

function backupExisting(targetFile) {
  if (!fs.existsSync(targetFile)) return null;
  const backup = targetFile + '.bak-' + Date.now();
  fs.copyFileSync(targetFile, backup);
  return backup;
}

function updateRegistry(registryPath, extensionPath) {
  let xml = fs.readFileSync(registryPath, 'utf8');
  const blockPattern = /[ \t]*<Item HRID="LCSCLoader"[\s\S]*?<\/Item>\r?\n?/;

  if (flags.uninstall) {
    fs.writeFileSync(registryPath, xml.replace(blockPattern, ''), 'utf8');
    return 'registry entry removed';
  }

  if (blockPattern.test(xml)) {
    xml = xml.replace(/<Version>[^<]*<\/Version>/, '<Version>' + buildVersion() + '</Version>');
    xml = xml.replace(/<VersionGuid>[^<]*<\/VersionGuid>/, '<VersionGuid>D7E4C9A6-5FB1-4D3C-BE80-0B4C3F9D5E12</VersionGuid>');
    fs.writeFileSync(registryPath, xml, 'utf8');
    return 'registry entry already present, refreshed';
  }

  const close = xml.lastIndexOf('</Extensions>');
  if (close < 0) throw new Error('Unexpected ExtensionsRegistry.xml layout: ' + registryPath);
  fs.writeFileSync(registryPath, xml.slice(0, close) + buildItem(extensionPath) + xml.slice(close), 'utf8');
  return 'registry entry added';
}

function main() {
  const layout = flagValue('--target', 'auto');
  if (!deployTarget.LAYOUTS.includes(layout)) {
    throw new Error('--target must be one of: ' + deployTarget.LAYOUTS.join(', '));
  }
  const explicitDir = flagValue('--target-dir', null);

  let sdk = null;
  try {
    sdk = adSdk.resolveSdkDir(cli);
  } catch (error) {
    if (layout === 'programdata' || explicitDir) {
      console.warn('warning: no SDK detected (' + error.message.split('\n')[0] + ')');
    } else {
      throw error;
    }
  }

  let target;
  if (explicitDir) {
    const dir = path.resolve(explicitDir);
    if (!fs.existsSync(dir)) throw new Error('--target-dir does not exist: ' + dir);
    target = { layout: 'system', dir, registry: null, sdk };
  } else {
    target = deployTarget.resolveTarget({
      layout,
      sdk,
      adVersion: cli.adVersion,
      preferSystem: true,
    });
  }

  console.log('layout: ' + target.layout);
  console.log('target: ' + target.dir);
  if (target.registry) console.log('registry: ' + target.registry);

  if (target.layout === 'system' && altiumRunning() && !flags.force && !flags.dryRun) {
    throw new Error('Altium Designer (X2.EXE) is running. Close it first, or pass --force.');
  }

  const files = deployTarget.sourcePaths(root);
  for (const file of files) {
    if (!fs.existsSync(file.source)) {
      throw new Error('Missing build input: ' + file.source + ' (run node build.js first)');
    }
  }

  // Validate the version chain before touching the target, so a mismatch fails even
  // on --dry-run instead of being discovered after the files are already copied.
  if (!flags.uninstall) {
    console.log('version: ' + buildVersion());
  }

  if (flags.uninstall) {
    if (flags.dryRun) {
      console.log('[dry-run] would remove files from ' + target.dir);
    } else {
      for (const file of files) {
        const deployed = path.join(target.dir, file.name);
        if (fs.existsSync(deployed)) fs.rmSync(deployed, { force: true });
      }
      if (target.registry) updateRegistry(target.registry, target.dir);
      console.log('LCSCLoader removed from ' + target.dir + '. Restart Altium Designer.');
    }
    return;
  }

  if (flags.dryRun) {
    for (const file of files) {
      console.log('[dry-run] ' + file.name + '  ' + sha256(file.source) + '  -> ' + target.dir);
    }
    console.log('[dry-run] no files written');
    return;
  }

  fs.mkdirSync(target.dir, { recursive: true });
  for (const file of files) {
    const deployed = path.join(target.dir, file.name);
    const backup = backupExisting(deployed);
    fs.copyFileSync(file.source, deployed);
    console.log('  ' + file.name + '  ' + sha256(deployed).slice(0, 12) + '…' +
      (backup ? '  (backup: ' + path.basename(backup) + ')' : ''));
  }

  if (target.registry) {
    console.log('  ' + updateRegistry(target.registry, target.dir));
  }

  console.log('Installed to: ' + target.dir);
  if (target.layout === 'system') {
    console.log('Start Altium Designer, then use Tools > LCSC Component Import... .');
  } else {
    console.log('Restart Altium Designer and confirm the extension is enabled in Extensions & Updates.');
  }
}

try {
  main();
} catch (error) {
  console.error('ERROR: ' + error.message);
  process.exitCode = 1;
}
