const fs = require('fs');

const code = fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');
const lines = code.split('\n');

console.log('Total lines:', lines.length);
console.log('\n=== Checking last 5 lines ===');
for (let i = Math.max(0, lines.length - 5); i < lines.length; i++) {
  console.log(`Line ${i + 1} (${lines[i].length} chars): ${lines[i].substring(0, 100)}`);
}

console.log('\n=== Checking line 235 ending ===');
const line235 = lines[234];
console.log('Line 235 length:', line235.length);
console.log('Last 100 chars:', line235.substring(Math.max(0, line235.length - 100)));
console.log('Contains closing backtick?', line235.includes('`'));

console.log('\n=== Looking for unclosed template literals ===');
let inTemplate = false;
let templateStart = -1;
for (let i = 0; i < line235.length; i++) {
  if (line235[i] === '`' && (i === 0 || line235[i-1] !== '\\')) {
    if (inTemplate) {
      console.log(`Template closed at position ${i}`);
      inTemplate = false;
    } else {
      console.log(`Template opened at position ${i}, context: ${line235.substring(Math.max(0, i - 30), i + 30)}`);
      templateStart = i;
      inTemplate = true;
    }
  }
}

if (inTemplate) {
  console.log('ERROR: Template literal still open at end of line!');
  console.log('Started at position:', templateStart);
  console.log('Content from start: ', line235.substring(templateStart, Math.min(templateStart + 100, line235.length)));
}
