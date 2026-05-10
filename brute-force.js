const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

function testCode(modLine235) {
  const modLines = [...lines];
  modLines[234] = modLine235;
  const modCode = modLines.join('\n');
  try {
    new vm.Script(modCode);
    return null; // no error
  } catch(e) {
    return e.message;
  }
}

// Try removing each ] in a range around the error (26500-26700)
console.log('=== Testing removing each ] ===');
for (let i = 26500; i < 26700; i++) {
  if (line235[i] === ']') {
    const modified = line235.substring(0, i) + line235.substring(i+1);
    const err = testCode(modified);
    if (!err) {
      console.log(`FIXED by removing ] at index ${i}!`);
      console.log('Context:', JSON.stringify(line235.substring(Math.max(0,i-30), i+30)));
    }
  }
}

// Try adding [ at each position in range 18700-21100 (before the IIFE)
console.log('\n=== Testing adding [ at various positions ===');
for (let i = 18700; i < 21100; i += 5) {
  const modified = line235.substring(0, i) + '[' + line235.substring(i);
  const err = testCode(modified);
  if (!err) {
    console.log(`FIXED by adding [ at index ${i}!`);
    console.log('Context:', JSON.stringify(line235.substring(Math.max(0,i-30), i+30)));
  }
}

console.log('Done searching');
