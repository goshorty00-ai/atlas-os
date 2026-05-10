const fs = require('fs');

const FILES = [
  'D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js',
  'D:/My Apps/AOS/Atlas.OS/bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js',
];

// Change 1: add _html variable after _rawBody declaration
const S1_OLD = '_rawBody=(typeof _d.bodyText==="string"&&_d.bodyText?_d.bodyText:typeof _d.snippet==="string"?_d.snippet:"No content."),_cleaned=';
const S1_NEW = '_rawBody=(typeof _d.bodyText==="string"&&_d.bodyText?_d.bodyText:typeof _d.snippet==="string"?_d.snippet:"No content."),_html=(typeof _d.htmlBody==="string"&&_d.htmlBody.length>0?_d.htmlBody:null),_cleaned=';

// Change 2: replace body card with iframe conditional
const S2_OLD = 'o.jsxs("div",{className:"relative rounded-2xl border border-cyan-400/14 bg-[linear-gradient(180deg,rgba(4,10,22,0.95),rgba(6,14,30,0.82))] shadow-[0_0_28px_rgba(34,211,238,0.045),inset_0_0_0_1px_rgba(99,102,241,0.06)] overflow-hidden",children:[o.jsx("div",{className:"absolute left-0 top-0 bottom-0 w-[3px] rounded-l-2xl bg-gradient-to-b from-cyan-400/50 via-indigo-400/30 to-transparent"}),o.jsx("div",{className:"h-px w-full bg-gradient-to-r from-transparent via-cyan-400/20 to-transparent"}),o.jsx("div",{className:"pl-5 pr-4 py-4 space-y-[0.95rem]",children:_bodyParas.map(function(p,i){return o.jsx("p",{className:"text-[13px] leading-[1.78] text-white/72 break-words",children:p},String(i));})}),_footerParas.length>0?o.jsxs("div",{className:"mx-4 mb-3 pt-3 border-t border-white/[0.06]",children:[_footerParas.map(function(p,i){return o.jsx("p",{className:"text-[11px] leading-relaxed text-white/28 break-words",children:p},"f"+String(i));})]}):null,o.jsx("div",{className:"h-px w-full bg-gradient-to-r from-transparent via-indigo-400/12 to-transparent"})]}),';

const S2_NEW = '_html?o.jsxs("div",{className:"relative rounded-2xl border border-cyan-400/20 bg-[linear-gradient(180deg,rgba(4,10,22,0.95),rgba(6,14,30,0.82))] shadow-[0_0_28px_rgba(34,211,238,0.06),inset_0_0_0_1px_rgba(99,102,241,0.08)] overflow-hidden",children:[o.jsx("div",{className:"absolute left-0 top-0 bottom-0 w-[3px] rounded-l-2xl bg-gradient-to-b from-cyan-400/60 via-indigo-400/40 to-transparent"}),o.jsxs("div",{className:"flex items-center justify-between px-4 py-2 border-b border-cyan-400/10",children:[o.jsx("span",{className:"text-[10px] uppercase tracking-[0.18em] text-cyan-400/50 font-semibold",children:"HTML email preview"}),o.jsx("span",{className:"text-[10px] text-white/20",children:"\u25cf sandboxed"})]}),o.jsx("iframe",{title:"Email body",sandbox:"allow-popups allow-popups-to-escape-sandbox",srcDoc:_html,style:{width:"100%",minHeight:"520px",border:"none",background:"#ffffff",display:"block"}})]}):o.jsxs("div",{className:"relative rounded-2xl border border-cyan-400/14 bg-[linear-gradient(180deg,rgba(4,10,22,0.95),rgba(6,14,30,0.82))] shadow-[0_0_28px_rgba(34,211,238,0.045),inset_0_0_0_1px_rgba(99,102,241,0.06)] overflow-hidden",children:[o.jsx("div",{className:"absolute left-0 top-0 bottom-0 w-[3px] rounded-l-2xl bg-gradient-to-b from-cyan-400/50 via-indigo-400/30 to-transparent"}),o.jsx("div",{className:"h-px w-full bg-gradient-to-r from-transparent via-cyan-400/20 to-transparent"}),o.jsx("div",{className:"pl-5 pr-4 py-4 space-y-[0.95rem]",children:_bodyParas.map(function(p,i){return o.jsx("p",{className:"text-[13px] leading-[1.78] text-white/72 break-words",children:p},String(i));})}),_footerParas.length>0?o.jsxs("div",{className:"mx-4 mb-3 pt-3 border-t border-white/[0.06]",children:[_footerParas.map(function(p,i){return o.jsx("p",{className:"text-[11px] leading-relaxed text-white/28 break-words",children:p},"f"+String(i));})]}):null,o.jsx("div",{className:"h-px w-full bg-gradient-to-r from-transparent via-indigo-400/12 to-transparent"})]}),';

for (const file of FILES) {
  let content = fs.readFileSync(file, 'utf8');

  const c1before = content.split(S1_OLD).length - 1;
  const c2before = content.split(S2_OLD).length - 1;
  console.log(`\n[${file}]`);
  console.log('  S1 occurrences:', c1before);
  console.log('  S2 occurrences:', c2before);

  if (c1before !== 1) { console.error('  ERROR: S1 not found exactly once'); process.exit(1); }
  if (c2before !== 1) { console.error('  ERROR: S2 not found exactly once'); process.exit(1); }

  content = content.replace(S1_OLD, S1_NEW);
  content = content.replace(S2_OLD, S2_NEW);

  // Verify applied
  const c1after = content.split(S1_NEW).length - 1;
  const c2after = content.split('_html?o.jsxs').length - 1;
  console.log('  After S1_NEW count:', c1after);
  console.log('  After _html?o.jsxs count:', c2after);

  fs.writeFileSync(file, content, 'utf8');
  console.log('  Written OK');
}
console.log('\nDone.');
