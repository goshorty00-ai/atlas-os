const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Track bracket balance from col 24000 to find what [ opens the ] at 26666
// Simple tracker (ignores strings/templates for now but should be ok for this context)
const startCol = 24000;
const errorCol = 26666;

let depth = { sq: 0, cu: 0, pa: 0 }; // square, curly, paren
let stack = []; // track each opening bracket position

let inStr = false, strCh = '';
let inTemplate = 0;

for (let i = startCol; i <= errorCol; i++) {
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
  else if (c === '[') { stack.push({c:'[', pos:i}); depth.sq++; }
  else if (c === ']') {
    if (stack.length > 0 && stack[stack.length-1].c === '[') {
      const opening = stack.pop();
      depth.sq--;
      if (i === errorCol) {
        console.log(`The ] at col ${i} closes the [ at col ${opening.pos}`);
        console.log('Context of opening [:', JSON.stringify(line235.substring(Math.max(0,opening.pos-50), opening.pos+50)));
      }
    } else {
      console.log(`UNEXPECTED ] at col ${i}, stack top:`, stack.length > 0 ? stack[stack.length-1] : 'empty');
    }
  }
  else if (c === '{') { stack.push({c:'{', pos:i}); depth.cu++; }
  else if (c === '}') {
    if (stack.length > 0 && stack[stack.length-1].c === '{') {
      stack.pop(); depth.cu--;
    } else {
      console.log(`Unexpected } at col ${i}, stack:`, JSON.stringify(stack.slice(-3)));
    }
  }
  else if (c === '(') { stack.push({c:'(', pos:i}); depth.pa++; }
  else if (c === ')') {
    if (stack.length > 0 && stack[stack.length-1].c === '(') {
      stack.pop(); depth.pa--;
    } else {
      console.log(`Unexpected ) at col ${i}`);
    }
  }
}

console.log('\nFinal state at col', errorCol, ':', JSON.stringify(depth));
console.log('Stack top 5:', JSON.stringify(stack.slice(-5)));
