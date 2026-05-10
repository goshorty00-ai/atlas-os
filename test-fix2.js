const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

function testCode(modLine235, desc) {
  const modLines = [...lines];
  modLines[234] = modLine235;
  try {
    new vm.Script(modLines.join('\n'));
    console.log(`${desc}: PARSES OK!`);
    return true;
  } catch(e) {
    console.log(`${desc}: ${e.message}`);
    return false;
  }
}

// Show the 3 chars at 26665-26667
console.log('Chars at 26665-26667:', JSON.stringify(line235.substring(26665, 26668)));
console.log('Context:', JSON.stringify(line235.substring(26655, 26680)));

// Test removing ] } ) at 26665-26667 (3 chars)
const r1 = line235.substring(0, 26665) + line235.substring(26668);
testCode(r1, 'Remove ]}) at 26665-26667');

// Test removing just ] at 26665
const r2 = line235.substring(0, 26665) + line235.substring(26666);
testCode(r2, 'Remove ] at 26665 only');

// Test removing }]) at 26660-26662 (the } and ) and ] before the ] at 26662)
const r3 = line235.substring(0, 26660) + line235.substring(26663);
testCode(r3, 'Remove })] at 26660-26662');

// Test: adding [ somewhere in range 21080-21095 (before return o.jsxs)
const retPos = 21084;
console.log('\nContext at return:', JSON.stringify(line235.substring(21082, 21096)));
for (let pos = 21084; pos <= 21094; pos++) {
  const mod = line235.substring(0, pos) + '[' + line235.substring(pos);
  testCode(mod, `Add [ at ${pos}`);
}
