const fs = require('fs');
const paths = [
  'D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js',
  'D:/My Apps/AOS/Atlas.OS/bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js'
];
const old = `children:"Email Address"}),o.jsx("input",{type:"email",value:b,onChange:Q=>B(Q.target.value),placeholder:"your.email@example.com"`;
const neu = `children:"Your Gmail address"}),o.jsx("input",{type:"email",value:b,onChange:Q=>B(Q.target.value),placeholder:"you@gmail.com"`;
for(const p of paths){
  let c = fs.readFileSync(p,'utf8');
  if(c.includes(old)){c=c.replace(old,neu);fs.writeFileSync(p,c);console.log(p+': label fixed');}
  else{console.log(p+': no match for label');}
}
