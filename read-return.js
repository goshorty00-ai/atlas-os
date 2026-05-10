const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show broader context of the return statement
console.log('Cols 21080-21600:');
console.log(line235.substring(21080, 21600));
