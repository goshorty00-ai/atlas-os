const vm=require('vm');
const fs=require('fs');
const raw=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');

// Full file has syntax error
// Binary search: find exact char position of the broken ]
// Strategy: test prefixes of increasing length, find where error FIRST appears
// Then narrow down to find the ] that's wrong

let lo=0, hi=raw.length;
let lastGood=0;

// Course binary search first
while(lo<hi-1){
  const mid=Math.floor((lo+hi)/2);
  // We need a self-contained snippet. Append a comment to close any open strings/templates
  // Actually, we can't do that easily for minified code.
  // Instead, test: is `prefix + rest` still an error? (different approach)
  // Let's find the LAST position where truncated code is VALID:
  // Append ';' to close the statement, then check
  const frag=raw.substring(0,mid)+'\n;';
  let ok=false;
  try{new vm.Script(frag);ok=true;}catch(e){ok=false;}
  if(ok){lo=mid;lastGood=mid;}else{hi=mid;}
}
console.log('Error boundary at char:', hi);
console.log('Last good at char:', lo);

// Get line/col for hi
const before=raw.substring(0,hi);
const lines=before.split('\n');
const lineNum=lines.length;
const col=lines[lines.length-1].length;
console.log(`Line: ${lineNum}, Col: ${col}`);
console.log('Context around error:', JSON.stringify(raw.substring(Math.max(0,hi-30),hi+30)));
