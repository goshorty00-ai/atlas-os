const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');

// Test 1: Remove ] at index 26665 in line235
let line235 = lines[234];
const removePos = 26665;
const char = line235[removePos];
console.log(`Removing char '${char}' at index ${removePos}`);

const modified235 = line235.substring(0, removePos) + line235.substring(removePos+1);
const modifiedLines = [...lines];
modifiedLines[234] = modified235;
const modCode = modifiedLines.join('\n');

try {
  new vm.Script(modCode);
  console.log('After removing ]: PARSES OK!');
} catch(e) {
  console.log('After removing ]: Still ERROR:', e.message);
  
  // Check where the new error is
  const modLines = modCode.split('\n');
  const modLine235 = modLines[234];
  const modPrefix = modLines.slice(0,234).join('\n')+'\n';
  for (let c=26600; c<=26700; c++) {
    const frag = modPrefix + modLine235.substring(0,c);
    let err=null;
    try{new vm.Script(frag);}catch(e2){err=e2;}
    if (err && (err.message.includes("Unexpected token") || err.message.includes("missing"))) {
      const prevFrag = modPrefix + modLine235.substring(0,c-1);
      let prevErr=null;
      try{new vm.Script(prevFrag);}catch(e3){prevErr=e3;}
      if (!prevErr || !prevErr.message.includes("Unexpected token")) {
        console.log(`New error first at col ${c}:`, err.message);
        console.log('Context:', JSON.stringify(modLine235.substring(c-20,c+20)));
        break;
      }
    }
  }
}
