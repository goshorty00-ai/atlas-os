// Bracket analysis v2
const fs = require('fs');
const code = fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

// Try to evaluate directly with acorn or using new Function
// Instead, let's track brackets carefully

let stack = [];
let inString = null;
let inTemplate = false;
let templateDepth = 0;
let i = 0;
let lastGoodPos = 0;
let line = 1;
let lineStart = 0;
let problems = [];

while (i < code.length) {
  const c = code[i];
  
  if (c === '\n') {
    line++;
    lineStart = i + 1;
  }
  
  // Handle escape sequences
  if (inString && c === '\\') {
    i += 2;
    continue;
  }
  
  // Handle string end
  if (inString && c === inString) {
    inString = null;
    i++;
    continue;
  }
  
  // While in regular string, skip
  if (inString) {
    i++;
    continue;
  }
  
  // Handle template literals
  if (c === '`') {
    if (inTemplate && templateDepth === 0) {
      inTemplate = false;
    } else {
      inTemplate = true;
    }
    i++;
    continue;
  }
  
  // Handle template expression start
  if (inTemplate && c === '$' && code[i+1] === '{') {
    templateDepth++;
    stack.push({ char: '{', line: line, col: i - lineStart });
    i += 2;
    continue;
  }
  
  // Skip template content (not in expression)
  if (inTemplate && templateDepth === 0) {
    i++;
    continue;
  }
  
  // Start of string
  if (c === '"' || c === "'") {
    inString = c;
    i++;
    continue;
  }
  
  // Opening brackets
  if (c === '(' || c === '[' || c === '{') {
    stack.push({ char: c, line: line, col: i - lineStart, pos: i });
    if (c === '{' && inTemplate) templateDepth++;
    i++;
    continue;
  }
  
  // Closing brackets
  if (c === ')' || c === ']' || c === '}') {
    const match = { ')': '(', ']': '[', '}': '{' }[c];
    if (stack.length === 0) {
      problems.push(`Line ${line}, col ${i - lineStart}: Unexpected '${c}' with empty stack`);
      if (problems.length >= 5) break;
      i++;
      continue;
    }
    const top = stack[stack.length - 1];
    if (top.char !== match) {
      problems.push(`Line ${line}, col ${i - lineStart}: Expected '${match === '(' ? ')' : match === '[' ? ']' : '}'}' but got '${c}' (mismatched from line ${top.line})`);
      if (problems.length >= 5) break;
    } else {
      stack.pop();
      if (c === '}' && inTemplate && templateDepth > 0) templateDepth--;
    }
    lastGoodPos = i;
    i++;
    continue;
  }
  
  i++;
}

console.log('Analysis complete');
console.log('Unclosed brackets:', stack.length);
if (stack.length > 0 && stack.length <= 10) {
  stack.forEach(b => console.log('  Unclosed:', b.char, 'at line', b.line, 'col', b.col));
}
if (problems.length > 0) {
  console.log('\nProblems found:');
  problems.forEach(p => console.log(' ', p));
} else {
  console.log('No mismatch problems found');
}
