const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

console.log('Line 235 length:', line235.length);

// Find all ] in range 950-1100
for (let i=950; i<1100; i++) {
  if (line235[i] === ']') {
    console.log('Found ] at col', i, 'context:', JSON.stringify(line235.substring(Math.max(0,i-20), i+20)));
  }
}

// Also check 1010-1030
console.log('\nAround col 1020:');
console.log(JSON.stringify(line235.substring(1000, 1045)));
