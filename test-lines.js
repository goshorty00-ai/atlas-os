const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');

// Test lines 1-234 only (up to but not including line 235)
const prefix234=lines.slice(0,234).join('\n');
console.log('Lines 1-234 length:', prefix234.length);

try {
  new vm.Script(prefix234);
  console.log('Lines 1-234: PARSE OK');
} catch(e) {
  console.log('Lines 1-234 ERROR:', e.message);
  // Extract line/col from stack
  const m = (e.stack||'').match(/evalmachine\.anonymous:(\d+)\n/);
  if (m) console.log('Error at eval line:', m[1]);
}

// Also test lines 1-241 (the full block including template)
const prefix241=lines.slice(0,241).join('\n');
console.log('\nLines 1-241 length:', prefix241.length);
try {
  new vm.Script(prefix241);
  console.log('Lines 1-241: PARSE OK');
} catch(e) {
  console.log('Lines 1-241 ERROR:', e.message);
  const m = (e.stack||'').match(/evalmachine\.anonymous:(\d+)\n(.*)/);
  if (m) console.log('Error at eval line:', m[1], 'content:', JSON.stringify(m[2] && m[2].substring(0,100)));
}
