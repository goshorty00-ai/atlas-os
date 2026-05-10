const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');

function test(description, modLine235) {
  const modLines = [...lines];
  modLines[234] = modLine235;
  const modCode = modLines.join('\n');
  try {
    new vm.Script(modCode);
    console.log(description + ': PARSES OK!');
    return true;
  } catch(e) {
    console.log(description + ': ERROR: ' + e.message);
    return false;
  }
}

let line235 = lines[234];

// Show context around 26655-26670
console.log('Actual chars 26655-26670:');
for (let i=26655; i<=26670; i++) {
  process.stdout.write(`[${i}]='${line235[i]}' `);
}
console.log();

// Try different removals/insertions
// Remove ] at 26665
const r1 = line235.substring(0, 26665) + line235.substring(26666);
test('Remove ] at 26665', r1);

// Remove ] at 26662
const r2 = line235.substring(0, 26662) + line235.substring(26663);
test('Remove ] at 26662', r2);

// Remove both ] at 26662 and 26665 (after first removal, 26665 → 26664)
const r3 = line235.substring(0, 26662) + line235.substring(26663, 26664) + line235.substring(26665);
test('Remove ] at 26662 and 26664', r3);

// Remove ] at 26659 (find what's there)
console.log('\nChar at 26659:', line235[26659]);
// Remove ]) at 26660-26661
const r4 = line235.substring(0, 26660) + line235.substring(26662);
test('Remove }] at 26660-26661', r4);
