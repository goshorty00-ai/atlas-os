const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show the code around col 1020 with more context
console.log('Cols 900-1050:');
console.log(JSON.stringify(line235.substring(900,1050)));

// Find all unescaped quotes to understand string boundaries
// Count quotes from position 0 to 1020
let inDQStr=false, inSQStr=false;
let strStart=-1;
for (let i=0; i<1050; i++){
  const c=line235[i];
  const prev=i>0?line235[i-1]:'';
  if (prev==='\\') continue; // skip escaped
  if (!inDQStr && !inSQStr && c==='"') {inDQStr=true; strStart=i;}
  else if (inDQStr && c==='"') {inDQStr=false;}
  else if (!inDQStr && !inSQStr && c==="'") {inSQStr=true; strStart=i;}
  else if (inSQStr && c==="'") {inSQStr=false;}
  
  if (i>=990 && i<=1060) {
    console.log(`Col ${i}: '${c}' inDQ=${inDQStr} inSQ=${inSQStr} strStart=${strStart}`);
  }
}
