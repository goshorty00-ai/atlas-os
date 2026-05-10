const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

const col=26666;
// Show much broader context to understand structure
console.log('Cols 26200-26800:');
console.log(line235.substring(26200, 26800));

// Also show what's just BEFORE the ] at 26665
console.log('\nSpecific context around the ] at 26666:');
console.log(JSON.stringify(line235.substring(26620, 26720)));
