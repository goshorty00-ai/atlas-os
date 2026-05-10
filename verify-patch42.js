const fs = require('fs');
const content = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

const s1 = '_rawBody=(typeof _d.bodyText==="string"&&_d.bodyText?_d.bodyText:typeof _d.snippet==="string"?_d.snippet:"No content."),_cleaned=';
const c1 = content.split(s1).length - 1;
console.log('Change1 count:', c1);

const s2 = 'o.jsxs("div",{className:"relative rounded-2xl border border-cyan-400/14 bg-[linear-gradient(180deg,rgba(4,10,22,0.95),rgba(6,14,30,0.82))] shadow-[0_0_28px_rgba(34,211,238,0.045),inset_0_0_0_1px_rgba(99,102,241,0.06)] overflow-hidden",children:[o.jsx("div",{className:"absolute left-0 top-0 bottom-0 w-[3px] rounded-l-2xl bg-gradient-to-b from-cyan-400/50 via-indigo-400/30 to-transparent"}),o.jsx("div",{className:"h-px w-full bg-gradient-to-r from-transparent via-cyan-400/20 to-transparent"}),o.jsx("div",{className:"pl-5 pr-4 py-4 space-y-[0.95rem]",children:_bodyParas.map(function(p,i){return o.jsx("p",{className:"text-[13px] leading-[1.78] text-white/72 break-words",children:p},String(i));})}),_footerParas.length>0?o.jsxs("div",{className:"mx-4 mb-3 pt-3 border-t border-white/[0.06]",children:[_footerParas.map(function(p,i){return o.jsx("p",{className:"text-[11px] leading-relaxed text-white/28 break-words",children:p},"f"+String(i));})]}):null,o.jsx("div",{className:"h-px w-full bg-gradient-to-r from-transparent via-indigo-400/12 to-transparent"})]}),';
const c2 = content.split(s2).length - 1;
console.log('Change2 count:', c2);
console.log('Change2 found:', c2 === 1 ? 'YES - unique' : (c2 === 0 ? 'NOT FOUND' : 'MULTIPLE'));
