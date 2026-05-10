const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Find "Ke&&m.selectedMessageDetail" in line 235
const pattern = 'Ke&&m.selectedMessageDetail';
let idx = 0;
while ((idx = line235.indexOf(pattern, idx)) !== -1) {
  console.log(`Found at col ${idx+1}: ${JSON.stringify(line235.substring(Math.max(0,idx-50), idx+80))}`);
  idx++;
}
