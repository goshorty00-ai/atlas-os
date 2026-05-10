const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show code around col 26657 (first unexpected ])
console.log('Cols 26580-26700:');
console.log(JSON.stringify(line235.substring(26580, 26700)));

// Track brackets from 24000 and find when stack first becomes empty
let stack = [];
let inStr = false, strCh = '';
let inTemplate = 0;

// Find the last position before col 26657 where stack was AT ITS MAXIMUM depth
// and the last [ before 26657

let maxDepth = 0;
let emptyAt = -1;
let lastOpenBracket = -1;

for (let i = 24000; i < 26700; i++) {
  const c = line235[i];
  const prev = i > 0 ? line235[i-1] : '';
  
  if (inStr) {
    if (c === strCh && prev !== '\\') inStr = false;
    continue;
  }
  if (inTemplate > 0) {
    if (c === '`' && prev !== '\\') inTemplate--;
    continue;
  }
  
  if (c === '"' || c === "'") { inStr = true; strCh = c; }
  else if (c === '`') { inTemplate++; }
  else if (c === '[') { stack.push(i); if(stack.length > maxDepth) maxDepth=stack.length; lastOpenBracket=i; }
  else if (c === ']') {
    if (stack.length > 0) { 
      const op = stack.pop();
      if (stack.length === 0 && emptyAt === -1) {
        emptyAt = i;
        console.log(`Square brackets first balanced at col ${i} (opened at ${op})`);
        console.log('Context of close:', JSON.stringify(line235.substring(i-20, i+20)));
        console.log('Context of open:', JSON.stringify(line235.substring(op-20, op+20)));
      }
    } else {
      console.log(`Extra ] at col ${i}, context:`, JSON.stringify(line235.substring(i-20, i+20)));
    }
  }
  else if (c === '{') { /* ignore */ }
  else if (c === '}') { /* ignore */ }
  else if (c === '(') { /* ignore */ }
  else if (c === ')') { /* ignore */ }
}

console.log(`Max depth: ${maxDepth}, emptyAt: ${emptyAt}`);
console.log(`Last open bracket before 26657: ${lastOpenBracket}`);
console.log('Context of last open bracket:', JSON.stringify(line235.substring(lastOpenBracket-30, lastOpenBracket+30)));
