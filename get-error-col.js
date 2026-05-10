const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');

try {
  new vm.Script(code, {filename:'test.js'});
} catch(e) {
  console.log('Error:', e.message);
  console.log('Line:', e.lineNumber || 'unknown');
  console.log('Column:', e.columnNumber || 'unknown');
  // Try to get more info
  if (e.loc) console.log('Loc:', JSON.stringify(e.loc));
  if (e.pos) console.log('Pos:', e.pos);
  
  // Print the error stack which may have position info
  console.log('Stack:', e.stack && e.stack.substring(0, 500));
}
