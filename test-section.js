const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');

// Extract lines 230-242 and test in isolation
const section = lines.slice(229, 242).join('\n');
console.log('Section lines 230-242:');
console.log('Total chars:', section.length);

// Write to a temp test file
const wrapper = '// Temp test\n' + section;
fs.writeFileSync('temp-test-section.js', wrapper);

const vm=require('vm');
try {
  new vm.Script(wrapper);
  console.log('Section PARSES OK');
} catch(e) {
  console.log('Section ERROR:', e.message);
  const stack = e.stack || '';
  const m = stack.match(/:(\d+)\n/);
  console.log('Error in test at line:', m ? m[1] : 'unknown');
}

// Also test the Ee function body in isolation
// Find what's on line 241
console.log('\nLine 241 first 200 chars:', JSON.stringify(lines[240].substring(0,200)));
console.log('Line 241 last 200 chars:', JSON.stringify(lines[240].substring(lines[240].length-200)));
