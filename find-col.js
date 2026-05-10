const fs=require('fs');
const vm=require('vm');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];
const prefix=lines.slice(0,234).join('\n')+'\n';

// Find the actual column where error occurs on line 235
// Try progressively adding chunks of line 235 to the prefix
// The prefix (lines 1-234) ends with a block comment,
// so prefix + " */" starts valid code

// Test prefix + specific amount of line 235
// Start from 3 (after "*/ ") to skip past the comment close
for (let col = 3; col <= line235.length; col += 500) {
  const frag = prefix + line235.substring(0, col);
  let err = null;
  try { new vm.Script(frag); } catch(e) { err=e; }
  if (err) {
    console.log(`Error appears between col ${Math.max(0,col-500)} and ${col}`);
    // Narrow down
    for (let c2 = col-500; c2 <= col; c2++) {
      const frag2 = prefix + line235.substring(0, c2);
      try { new vm.Script(frag2); } catch(e2) {
        console.log(`Exact error at col ${c2}:`, e2.message);
        console.log('Context:', JSON.stringify(line235.substring(Math.max(0,c2-30), c2+30)));
        process.exit(0);
      }
    }
    console.log('Could not narrow further');
    process.exit(1);
  }
}
console.log('No error found - full line 235 valid with prefix');
