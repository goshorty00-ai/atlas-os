const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];
const line241=lines[240]; // "Best regards,`; if(...)"

// Full analysis from col 3 of line 235 through the whole template + rest of code
// Note: template literal starts at ~36524 on line 235
// Content of template is on lines 236-240
// Template closes at start of line 241 "Best regards,`;"

// Build the full text from line 235 col 3 to end of line 241
const fullSeg = line235.substring(3) + '\n' + 
  lines[235] + '\n' + // line 236
  lines[236] + '\n' + // line 237
  lines[237] + '\n' + // line 238
  lines[238] + '\n' + // line 239
  lines[239] + '\n' + // line 240
  line241;            // line 241

let stack = [];
let inStr = false, strCh = '';
let inTemplate = 0;
let templateExpr = 0; // depth inside ${...}

// Track only at specific positions for deep analysis
let errors = [];

for (let i = 0; i < fullSeg.length; i++) {
  const c = fullSeg[i];
  const prev = i > 0 ? fullSeg[i-1] : '';
  const absCol = 3 + i; // absolute col on original line 235 (for first line segment)
  
  if (inStr) {
    if (c === strCh && prev !== '\\') inStr = false;
    continue;
  }
  
  if (inTemplate > 0 && templateExpr === 0) {
    // Inside template literal, but not in ${...}
    if (c === '`' && prev !== '\\') inTemplate--;
    else if (c === '$' && fullSeg[i+1] === '{') {
      templateExpr++;
      i++; // skip the {
    }
    continue;
  }
  
  if (c === '"' || c === "'") { inStr = true; strCh = c; }
  else if (c === '`') { inTemplate++; }
  else if (c === '[') { stack.push({c:'[', pos:i, absPos:3+i}); }
  else if (c === ']') {
    if (stack.length > 0 && stack[stack.length-1].c === '[') {
      stack.pop();
    } else {
      errors.push({type:'extra]', pos:i, absPos:3+i});
    }
  }
  else if (c === '{') { 
    if (inTemplate > 0) templateExpr++;
    stack.push({c:'{', pos:i, absPos:3+i}); 
  }
  else if (c === '}') {
    if (inTemplate > 0 && templateExpr > 0) templateExpr--;
    if (stack.length > 0 && stack[stack.length-1].c === '{') {
      stack.pop();
    } else {
      errors.push({type:'extra}', pos:i, absPos:3+i});
    }
  }
  else if (c === '(') { stack.push({c:'(', pos:i, absPos:3+i}); }
  else if (c === ')') {
    if (stack.length > 0 && stack[stack.length-1].c === '(') {
      stack.pop();
    } else {
      errors.push({type:'extra)', pos:i, absPos:3+i});
    }
  }
}

console.log(`Stack at end: ${stack.length} unclosed brackets`);
stack.slice(-10).forEach(s => {
  const ctx = fullSeg.substring(Math.max(0,s.pos-20), s.pos+20);
  console.log(`  ${s.c} at pos ${s.pos} (col ${s.absPos}): ${JSON.stringify(ctx)}`);
});

console.log(`\nErrors found: ${errors.length}`);
errors.slice(0, 10).forEach(e => {
  const ctx = fullSeg.substring(Math.max(0,e.pos-30), e.pos+30);
  console.log(`  ${e.type} at pos ${e.pos} (col ${e.absPos}): ${JSON.stringify(ctx)}`);
});
