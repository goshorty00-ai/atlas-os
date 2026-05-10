const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Look for the structure around the closing sequence
// Find what JSX elements are closing at each ]})

// Trace from col 23000 to 26700 tracking ALL bracket types
let stack = [];
let inStr = false, strCh = '';
let inTemplate = 0;
let inTemplExpr = 0;
const startCol = 23000;
const events = [];

for (let i = startCol; i < 26700; i++) {
  const c = line235[i];
  const prev = i > 0 ? line235[i-1] : '';
  
  if (inStr) {
    if (c === strCh && prev !== '\\') inStr = false;
    continue;
  }
  if (inTemplate > 0 && inTemplExpr === 0) {
    if (c === '`' && prev !== '\\') inTemplate--;
    else if (c === '$' && line235[i+1] === '{') { inTemplExpr++; i++; }
    continue;
  }
  
  if (c === '"' || c === "'") { inStr = true; strCh = c; }
  else if (c === '`') { inTemplate++; }
  else if (c === '[') { stack.push({c:'[', pos:i}); }
  else if (c === ']') {
    if (stack.length > 0 && stack[stack.length-1].c === '[') {
      const op = stack.pop();
      if (i >= 26600) {
        console.log(`] at ${i} closes [ at ${op.pos}`);
        console.log('  Context of [:', JSON.stringify(line235.substring(Math.max(0,op.pos-40), op.pos+30)));
        console.log('  Context of ]:', JSON.stringify(line235.substring(Math.max(0,i-10), i+15)));
      }
    } else {
      console.log(`EXTRA ] at ${i}:`, JSON.stringify(line235.substring(Math.max(0,i-20), i+15)));
    }
  }
  else if (c === '{') { 
    if (inTemplate > 0) inTemplExpr++;
    stack.push({c:'{', pos:i}); 
  }
  else if (c === '}') {
    if (inTemplate > 0 && inTemplExpr > 0) inTemplExpr--;
    if (stack.length > 0 && stack[stack.length-1].c === '{') {
      stack.pop();
    } else {
      if (i >= 26600) console.log(`EXTRA } at ${i}`);
    }
  }
  else if (c === '(') { stack.push({c:'(', pos:i}); }
  else if (c === ')') {
    if (stack.length > 0 && stack[stack.length-1].c === '(') {
      stack.pop();
    } else {
      if (i >= 26600) console.log(`EXTRA ) at ${i}`);
    }
  }
}

console.log('\nRemaining stack at col 26700:', stack.length, 'items');
stack.slice(-5).forEach(s => console.log('  ', JSON.stringify(s)));
