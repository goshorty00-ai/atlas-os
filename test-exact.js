const vm=require('vm');
const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];
const prefix=lines.slice(0,234).join('\n')+'\n';

// Test at exactly col 26665 and 26666
for (const col of [26663, 26664, 26665, 26666, 26667, 26668]) {
  const frag = prefix + line235.substring(0, col);
  let err = null;
  try { new vm.Script(frag); } catch(e) { err = e; }
  console.log(`Col ${col}: last char='${line235[col-1]}' error=${err ? err.message : 'none'}`);
}
