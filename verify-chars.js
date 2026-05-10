const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Find "});})():" in line235 and show exact position
const pat = '});})():';
let idx = line235.indexOf(pat);
while (idx !== -1) {
  console.log(`Found at 0-indexed ${idx} (col ${idx+1}): before=${JSON.stringify(line235.substring(idx-10,idx))}, match=${JSON.stringify(line235.substring(idx,idx+20))}`);
  idx = line235.indexOf(pat, idx+1);
}

// Also show raw chars at 26660-26680
console.log('\nRaw chars 26660-26680:');
for (let i=26660; i<26681; i++) {
  const c = line235.charCodeAt(i);
  console.log(`  [${i}] = '${line235[i]}' (${c})`);
}
