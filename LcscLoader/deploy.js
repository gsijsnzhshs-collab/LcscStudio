#!/usr/bin/env node
// Installs (or removes) the LCSCLoader extension for Altium Designer.
// Run from an elevated shell: node deploy.js [--uninstall]
// Altium Designer must be closed while this runs.

const fs = require('fs');
const path = require('path');

const root = __dirname;
const uninstall = process.argv.includes('--uninstall');
const programData = process.env.ProgramData || 'C:\\ProgramData';
const altiumRoot = path.join(programData, 'Altium');
const FILES = [
  { source: path.join(root, 'bin', 'LcscLoader.dll'), name: 'LcscLoader.dll' },
  { source: path.join(root, 'LCSCLoader.Ins'), name: 'LCSCLoader.Ins' },
  { source: path.join(root, 'LcscLoader.rcs'), name: 'LcscLoader.rcs' },
];

function findExtensionsDir() {
  if (!fs.existsSync(altiumRoot)) {
    throw new Error('Altium data directory not found: ' + altiumRoot);
  }
  const candidates = fs.readdirSync(altiumRoot)
    .filter(name => name.startsWith('Altium Designer '))
    .map(name => path.join(altiumRoot, name, 'Extensions'))
    .filter(dir => fs.existsSync(path.join(dir, 'ExtensionsRegistry.xml')));
  if (candidates.length === 0) {
    throw new Error('No ExtensionsRegistry.xml found under ' + altiumRoot);
  }
  return candidates[0];
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
    '    <Version>1.0.3.0</Version>',
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

function main() {
  const extensionsDir = findExtensionsDir();
  const target = path.join(extensionsDir, 'LCSCLoader');
  const registryPath = path.join(extensionsDir, 'ExtensionsRegistry.xml');
  let xml = fs.readFileSync(registryPath, 'utf8');
  const blockPattern = /[ \t]*<Item HRID="LCSCLoader"[\s\S]*?<\/Item>\r?\n?/;

  if (uninstall) {
    xml = xml.replace(blockPattern, '');
    fs.writeFileSync(registryPath, xml, 'utf8');
    fs.rmSync(target, { recursive: true, force: true });
    console.log('LCSCLoader removed. Restart Altium Designer.');
    return;
  }

  for (const file of FILES) {
    if (!fs.existsSync(file.source)) {
      throw new Error('Missing build output: ' + file.source + ' (run the build first)');
    }
  }

  fs.mkdirSync(target, { recursive: true });
  for (const file of FILES) {
    fs.copyFileSync(file.source, path.join(target, file.name));
  }

  fs.copyFileSync(registryPath, registryPath + '.bak');
  if (blockPattern.test(xml)) {
    xml = xml.replace(/<Version>[^<]*<\/Version>/, '<Version>1.0.3.0</Version>');
    xml = xml.replace(/<VersionGuid>[^<]*<\/VersionGuid>/, '<VersionGuid>D7E4C9A6-5FB1-4D3C-BE80-0B4C3F9D5E12</VersionGuid>');
    fs.writeFileSync(registryPath, xml, 'utf8');
    console.log('Registry entry already present, files refreshed.');
  } else {
    const close = xml.lastIndexOf('</Extensions>');
    if (close < 0) {
      throw new Error('Unexpected ExtensionsRegistry.xml layout');
    }
    xml = xml.slice(0, close) + buildItem(target) + xml.slice(close);
    fs.writeFileSync(registryPath, xml, 'utf8');
    console.log('Registry entry added.');
  }

  console.log('Installed to: ' + target);
  console.log('Start Altium Designer, then use Tools > LCSC Component Import... in a schematic, schematic library, PCB or PCB library document.');
}

try {
  main();
} catch (error) {
  console.error('ERROR: ' + error.message);
  process.exitCode = 1;
}
