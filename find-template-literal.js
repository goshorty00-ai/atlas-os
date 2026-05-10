const fs = require('fs');

const code = fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');
const lines = code.split('\n');
const line = lines[234];

console.log('Line 235 length:', line.length);

// Find vt=`
const idx = line.lastIndexOf('vt=`');
if (idx > -1) {
  console.log('Found vt=` at position:', idx);
  const snippet = line.substring(idx, Math.min(idx + 200, line.length));
  console.log('Content (first 200 chars after vt=`):\n', snippet);
  
  // Look for the closing backtick
  const afterVt = line.substring(idx + 3);
  const closeIdx = afterVt.indexOf('`');
  if (closeIdx > -1) {
    console.log('\nFound closing backtick at position:', closeIdx);
    console.log('Full template literal:', afterVt.substring(0, closeIdx + 50));
  } else {
    console.log('\n❌ NO CLOSING BACKTICK FOUND - THIS IS THE ISSUE!');
    console.log('\nLast 300 chars of line 235:');
    console.log(line.substring(line.length - 300));
  }
}
