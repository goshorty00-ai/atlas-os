const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];
const prefix=lines.slice(0,234).join('\n')+'\n';

// The children: at col 18750 currently has NO [
// Looking at the context, there's children:Ke&&m.selectedMessageDetail?
// The ] at col 26665 closes something
// Try adding [ at col 18750 (just before Ke&&m.selectedMessageDetail)

const insertPos = 18750; // 0-indexed position in line235 to insert [
// line235[18750] should be 'K' (start of Ke&&m...)
console.log('Char at insertPos:', JSON.stringify(line235[insertPos]));
console.log('Context at insertPos:', JSON.stringify(line235.substring(insertPos-10, insertPos+30)));

// Create a modified line235 with [ inserted
const modified235 = line235.substring(0, insertPos) + '[' + line235.substring(insertPos);
console.log('Modified length:', modified235.length, '(was', line235.length, ')');

// Test if modified version parses correctly
const modifiedLines = [...lines];
modifiedLines[234] = modified235;
const modifiedCode = modifiedLines.join('\n');

try {
  new vm.Script(modifiedCode);
  console.log('MODIFIED CODE PARSES OK!');
} catch(e) {
  console.log('MODIFIED CODE ERROR:', e.message);
  // Try to find where the error is now
  const modLines = modifiedCode.split('\n');
  const modLine235 = modLines[234];
  const modPrefix = modLines.slice(0,234).join('\n')+'\n';
  
  // Binary search on new line 235
  for (let c=26000; c<=26800; c++) {
    const frag = modPrefix + modLine235.substring(0,c);
    let err=null;
    try{new vm.Script(frag);}catch(e2){err=e2;}
    if (err && err.message.includes("Unexpected token ']'")) {
      console.log(`New error at col ${c}:`, err.message);
      console.log('Context:', JSON.stringify(modLine235.substring(c-30,c+30)));
      break;
    }
  }
}
