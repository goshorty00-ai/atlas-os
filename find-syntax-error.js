const fs = require('fs');
const path = require('path');

const bundlePath = 'Figma/Email/dist/assets/index-C4iwRHDc.js';
const code = fs.readFileSync(bundlePath, 'utf8');
const lines = code.split('\n');

console.log(`Total lines: ${lines.length}`);
const line235 = lines[234]; // 0-indexed
console.log(`Line 235 length: ${line235.length} characters`);

// Find where the syntax error occurs by trying to parse progressively
let lastGoodPos = 0;
for (let i = 100; i < line235.length; i += 100) {
  const partial = line235.substring(0, i);
  try {
    new Function(partial);
    lastGoodPos = i;
  } catch (e) {
    if (e.message.includes('Unexpected token') && e.message.includes(']')) {
      console.log(`\nSyntax error found around position ${i}`);
      console.log(`Last good position: ${lastGoodPos}`);
      
      // Binary search for exact position
      let start = Math.max(lastGoodPos, 0);
      let end = Math.min(i + 100, line235.length);
      
      for (let j = start; j < end; j++) {
        const test = line235.substring(0, j);
        try {
          new Function(test);
        } catch (e2) {
          if (e2.message.includes(']')) {
            console.log(`\nExact error position: ${j}`);
            const contextStart = Math.max(0, j - 200);
            const contextEnd = Math.min(line235.length, j + 200);
            const context = line235.substring(contextStart, contextEnd);
            console.log(`\nContext (200 chars before and after error):`);
            console.log(`...${context}...`);
            console.log(`\nError location marked at character ${j - contextStart}:`);
            console.log(` `.repeat(j - contextStart) + '^ ERROR HERE');
            process.exit(0);
          }
        }
      }
      break;
    }
  }
}

console.log('No syntax error found in progressive parsing');
