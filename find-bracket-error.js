const fs=require('fs');
const vm=require('vm');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];
const prefix=lines.slice(0,234).join('\n')+'\n';

// Find where "Unexpected token ']'" first appears
// Check line 235 in chunks, and also lines 236-241
const allLines = lines.slice(234, 241);  // lines 235-241 (0-indexed 234-240)
let accumulated = prefix;

for (let li=0; li<allLines.length; li++){
  const ln = allLines[li];
  for (let c=0; c<=ln.length; c++){
    const frag = accumulated + ln.substring(0, c);
    let err = null;
    try { new vm.Script(frag); } catch(e) { err=e; }
    if (err && err.message === "Unexpected token ']'") {
      const lineActual = 235 + li;
      console.log(`"Unexpected token ']'" first appears at line ${lineActual}, col ${c}`);
      console.log('Context:', JSON.stringify(ln.substring(Math.max(0,c-40), c+40)));
      process.exit(0);
    }
  }
  accumulated += ln + '\n';
  console.log(`Line ${235+li} processed (${ln.length} chars), no ']' error yet`);
}

console.log('No "]" error found in lines 235-241!');
