const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

const col=26666;
// Show broader context
console.log('Cols 26500-26750:');
console.log(line235.substring(26500, 26750));
