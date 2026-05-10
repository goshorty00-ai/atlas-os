const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');

// Track block comments
let commentDepth = 0;
let inString = false;
let stringChar = '';
let inTemplate = 0;
let i = 0;
let lineNum = 1;
let lineStart = 0;

const commentStarts = [];
const commentEnds = [];

while (i < code.length) {
  const c = code[i];
  const c2 = code.substring(i, i+2);
  
  if (c === '\n') { lineNum++; lineStart = i+1; }
  
  if (inString) {
    if (c === stringChar && (i===0 || code[i-1] !== '\\')) {
      inString = false;
    }
    i++; continue;
  }
  
  if (inTemplate > 0) {
    if (c === '`' && (i===0 || code[i-1] !== '\\')) {
      inTemplate--;
    }
    i++; continue;
  }
  
  if (commentDepth > 0) {
    if (c2 === '*/') {
      commentDepth--;
      commentEnds.push({line: lineNum, col: i - lineStart, pos: i});
      i += 2; continue;
    }
    i++; continue;
  }
  
  // Not in string/template/comment
  if (c2 === '/*') {
    commentDepth++;
    commentStarts.push({line: lineNum, col: i - lineStart, pos: i});
    i += 2; continue;
  }
  if (c2 === '//') {
    // Line comment - skip to end of line
    while (i < code.length && code[i] !== '\n') i++;
    continue;
  }
  if (c === '"' || c === "'") {
    inString = true; stringChar = c;
    i++; continue;
  }
  if (c === '`') {
    inTemplate++;
    i++; continue;
  }
  i++;
}

// Show comments around lines 220-242
console.log('Block comment events:');
[...commentStarts.map(x=>({...x, type:'START'})), ...commentEnds.map(x=>({...x, type:'END'}))]
  .sort((a,b)=>a.pos-b.pos)
  .filter(x => x.line >= 220 && x.line <= 242)
  .forEach(x => console.log(`Line ${x.line} col ${x.col}: ${x.type}`));

console.log('\nFinal state: commentDepth =', commentDepth, 'inString =', inString, 'inTemplate =', inTemplate);
