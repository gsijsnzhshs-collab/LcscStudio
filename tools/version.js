'use strict';
// Single source of truth for the extension version. Edit version.json only.
// build.js generates BuildVersion.cs and renders LCSCLoader.Ins from it, and
// deploy.js reads the same value when writing ExtensionsRegistry.xml.

const fs = require('fs');
const path = require('path');

const FILE = path.join(__dirname, '..', 'version.json');
const PATTERN = /^\d+\.\d+\.\d+\.\d+$/;
const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

function read() {
  const raw = JSON.parse(fs.readFileSync(FILE, 'utf8'));
  const version = String(raw.version || '').trim();
  if (!PATTERN.test(version)) {
    throw new Error('version.json: "version" must be four numeric parts (e.g. 1.0.3.0), got "' +
      version + '"');
  }
  return version;
}

// AD expects the .Ins Date as '05-November-2026'.
function insDate(date) {
  const day = String(date.getDate()).padStart(2, '0');
  return day + '-' + MONTHS[date.getMonth()] + '-' + date.getFullYear();
}

module.exports = { FILE, read, insDate };
