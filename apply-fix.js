const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');

// Verify the fix string
const search = 'null]})})]})]});})():o.jsx';
const replace = 'null]})})]}]);})():o.jsx';

const count = code.split(search).length - 1;
console.log('Occurrences of search string:', count);
if (count === 1) {
  const fixed = code.replace(search, replace);
  // Verify
  const vm2 = require('vm');
  try {
    new vm2.Script(fixed);
    console.log('FIXED CODE PARSES OK!');
    fs.writeFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js', fixed);
    console.log('Source file updated!');
    
    // Also update bin copy
    const binPath = 'bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js';
    if (require('fs').existsSync(binPath)) {
      const binCode = require('fs').readFileSync(binPath, 'utf8');
      if (binCode.includes(search)) {
        const fixedBin = binCode.replace(search, replace);
        fs.writeFileSync(binPath, fixedBin);
        console.log('Bin file updated!');
      } else {
        console.log('WARNING: Search string not found in bin file!');
      }
    } else {
      console.log('Bin path not found:', binPath);
    }
  } catch(e) {
    console.log('ERROR after fix:', e.message);
  }
} else {
  console.log('ERROR: Need exactly 1 occurrence');
  // Show all positions
  let idx = 0;
  while ((idx = code.indexOf(search, idx)) !== -1) {
    console.log('Found at pos', idx);
    idx++;
  }
}
