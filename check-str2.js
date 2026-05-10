const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show content around where the string starting at col 929 should close
console.log('Cols 1045-1200:');
console.log(JSON.stringify(line235.substring(1045,1200)));

// And also find where string at 929 ends
let inStr=false;
for (let i=929; i<1300; i++){
  const c=line235[i];
  const prev=i>0?line235[i-1]:'';
  if (!inStr && c==='"') {inStr=true;}
  else if (inStr && c==='"' && prev!=='\\') {
    console.log(`String from 929 closes at col ${i}`);
    console.log('String content:', JSON.stringify(line235.substring(929, i+1)));
    break;
  }
}
