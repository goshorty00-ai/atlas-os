const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');

// Binary search within lines 1-234 to find where the error is
let lo=0, hi=234;
while (lo < hi - 1) {
  const mid = Math.floor((lo+hi)/2);
  const frag = lines.slice(0, mid).join('\n');
  let ok = false;
  try { new vm.Script(frag); ok=true; } catch(e) { ok=false; }
  if (ok) { lo=mid; } else { hi=mid; }
}
console.log('First error at line:', hi+1);
console.log('Lines 1-' + hi + ':');
console.log('Last few lines before error:');
for (let i=Math.max(0,hi-3); i<=hi; i++) {
  console.log(`Line ${i+1} (${lines[i].length} chars):`, JSON.stringify(lines[i].substring(0,100)));
}
