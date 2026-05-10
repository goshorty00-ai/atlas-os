const fs = require('fs');
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

console.log('=== vp (AI PANEL) COMPONENT ===');
const idx = c.indexOf('function vp(');
const vpBody = c.substring(idx, idx+8000);
console.log('postMessage in vp:', vpBody.includes('postMessage'));
console.log('ai-action in vp:', vpBody.includes('ai-action'));
console.log('email-ai in vp:', vpBody.includes('email-ai'));
console.log('fetch in vp:', vpBody.includes('fetch('));

// Find action handler  
const eeIdx = vpBody.indexOf('Ee=me=>');
console.log('\nEe (action handler):', vpBody.substring(eeIdx, eeIdx+600));

// check output area
const outIdx = vpBody.indexOf('OUTPUT');
console.log('\nOUTPUT area:', vpBody.substring(outIdx, outIdx+400));

// Show all static text in output
const draftIdx = vpBody.indexOf('Draft reply');
console.log('\nDraft reply:', vpBody.substring(draftIdx, draftIdx+200));

console.log('\n=== WPF BACKEND - email-ai/digest/analyze ===');
const wpf = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Modules/Email/EmailHostView.xaml.cs', 'utf8');
const terms = ['email-ai','digest','summarize','analyze-email','command-brief','command brief','ai digest','ai-digest','rules-scan'];
terms.forEach(t => {
  const i = wpf.indexOf(t);
  console.log(t + ':', i !== -1 ? 'FOUND at ' + i : 'NOT FOUND');
});

console.log('\n=== SERVICES DIRECTORY ===');
const svcDir = 'D:/My Apps/AOS/Atlas.OS/Modules/Email/Services';
try {
  fs.readdirSync(svcDir).forEach(f => console.log(' ', f));
} catch(e) { console.log('Cannot read:', e.message); }
