const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// More precise: try adding [ at every position in 18740-18760
console.log('=== Testing adding [ at positions 18740-18760 ===');
for (let i = 18740; i <= 18760; i++) {
  const modified = line235.substring(0, i) + '[' + line235.substring(i);
  const modLines = [...lines];
  modLines[234] = modified;
  try {
    new vm.Script(modLines.join('\n'));
    console.log(`FIXED at pos ${i}! Context: ${JSON.stringify(line235.substring(i-5, i+5))}`);
  } catch(e) {
    // Check if error is different from original
    if (!e.message.includes("Unexpected token ']'")) {
      console.log(`Pos ${i}: Error changed to: ${e.message.substring(0,50)}`);
    }
  }
}

// Also: maybe the fix requires removing ] somewhere AND/OR adding [
// Let's try: remove ] at each position 26600-26700 AND see what happens
console.log('\n=== Removing ] at 26500-26700 and checking result ===');
for (let i = 26500; i < 26700; i++) {
  if (line235[i] === ']') {
    const modified = line235.substring(0, i) + line235.substring(i+1);
    const modLines = [...lines];
    modLines[234] = modified;
    try {
      new vm.Script(modLines.join('\n'));
      console.log(`FIXED by removing ] at ${i}! Context: ${JSON.stringify(line235.substring(i-10,i+10))}`);
    } catch(e) {
      console.log(`Removing ] at ${i}: ${e.message.substring(0,50)}`);
    }
  }
}
