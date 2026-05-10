const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show exact chars at indices 26655-26675
console.log('Chars at indices 26655-26675:');
for (let i=26655; i<26676; i++) {
  console.log(`  Index ${i} (col ${i+1}): '${line235[i]}'`);
}

console.log('\nContext as string:', JSON.stringify(line235.substring(26640, 26680)));
