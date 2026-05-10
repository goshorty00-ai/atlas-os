const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show code around col 26666 error position  
// The ] at 26666 should close a children:[ opened before 26200
// Let's trace back from 26666 to find the matching [

// First, show cols around 25700-26300 to understand structure
console.log('Cols 25700-26200:');
console.log(line235.substring(25700, 26200));
