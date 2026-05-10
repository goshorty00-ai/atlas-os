const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// The sequence to fix:
// Original: null]})})]})]});})():o.jsx
// Fixed:    null]})})]}]);})():o.jsx
// Diff: remove ]) at positions that are extra
// Chars to remove: ] at 26665, } at 26666, ) at 26667

// Use the exact index-based approach
const removeStart = 26665;
const removeEnd = 26668; // exclusive (remove 26665, 26666, 26667)

const fixed235 = line235.substring(0, removeStart) + line235.substring(removeEnd);
console.log('Original chars at 26662-26670:', JSON.stringify(line235.substring(26662, 26671)));
console.log('Fixed chars at 26662-26668:', JSON.stringify(fixed235.substring(26662, 26668)));
console.log('Length change:', fixed235.length - line235.length, '(should be -3)');

// Test
const modLines = [...lines];
modLines[234] = fixed235;
const modCode = modLines.join('\n');
try {
  new vm.Script(modCode);
  console.log('\nFIXED CODE PARSES OK!');
  
  // Apply to source file
  const originalCode = fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
  const fixedCode = originalCode.replace(
    line235,
    fixed235
  );
  
  if (fixedCode === originalCode) {
    console.log('ERROR: Source file unchanged!');
  } else {
    fs.writeFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js', fixedCode);
    console.log('Source file updated!');
    
    // Apply to bin copy
    const binPath = 'bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js';
    if (fs.existsSync(binPath)) {
      const binCode = fs.readFileSync(binPath, 'utf8');
      const binLines = binCode.split('\n');
      const binLine235 = binLines[234];
      const fixed235Bin = binLine235.substring(0, removeStart) + binLine235.substring(removeEnd);
      const modBinLines = [...binLines];
      modBinLines[234] = fixed235Bin;
      const fixedBin = modBinLines.join('\n');
      fs.writeFileSync(binPath, fixedBin);
      console.log('Bin file updated!');
    } else {
      console.log('Bin path not found:', binPath);
    }
  }
} catch(e) {
  console.log('ERROR:', e.message);
}
