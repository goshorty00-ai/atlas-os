const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show code before the current section
console.log('Cols 25200-25750:');
console.log(line235.substring(25200, 25750));
