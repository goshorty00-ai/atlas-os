const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show content near the end of line 235 (before template opens at ~36524)
console.log('Cols 36400-36537:');
console.log(JSON.stringify(line235.substring(36400,36537)));

// Find all ] from col 36000 onwards
console.log('\n] positions from col 36000:');
for (let i=36000; i<line235.length; i++) {
  if (line235[i]===']') {
    console.log(`Col ${i}: context=${JSON.stringify(line235.substring(Math.max(0,i-30),i+30))}`);
  }
}
